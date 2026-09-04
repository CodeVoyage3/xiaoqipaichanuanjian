using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.IO;
using StoreExpiryInspector.Application;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;
using StoreExpiryInspector.Infrastructure.Excel;

namespace StoreExpiryInspector.Application.Imports;

public sealed record ConfirmedImportLifecycleRequest(
    ImportConfirmationContract Contract,
    string SnapshotDirectory,
    DateTime ParsedAtUtc,
    DateOnly BusinessDate,
    DateTime OccurredAtUtc);

public sealed class ConfirmedImportLifecycleOrchestrator
{
    private const int SqliteParameterBatchSize = 500;
    private const int PostImportProductGroupBatchSize = 250;
    private readonly ConfirmedImportExecutor? _executor;
    private readonly ProductStockZeroLifecycleUseCase _stockZeroLifecycle;
    private readonly PostImportLifecycleUseCase _postImportLifecycle;
    private readonly ColdStartScopeBaselineUseCase _coldStart;
    private readonly Action<string, TimeSpan>? _measure;

    public ConfirmedImportLifecycleOrchestrator(
        ConfirmedImportExecutor? executor = null,
        ProductStockZeroLifecycleUseCase? stockZeroLifecycle = null,
        PostImportLifecycleUseCase? postImportLifecycle = null,
        ColdStartScopeBaselineUseCase? coldStart = null,
        Action<string, TimeSpan>? measure = null)
    {
        _executor = executor;
        _stockZeroLifecycle = stockZeroLifecycle ?? new ProductStockZeroLifecycleUseCase();
        _postImportLifecycle = postImportLifecycle ?? new PostImportLifecycleUseCase();
        _coldStart = coldStart ?? new ColdStartScopeBaselineUseCase();
        _measure = measure;
    }

    public ConfirmedImportResult Execute(
        StoreDbContext context,
        ConfirmedImportLifecycleRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        FrozenImportFacts frozenFacts;
        try
        {
            frozenFacts = Measure("freeze_facts", () => FreezeFacts(context, request.Contract.Plan));
        }
        catch (InvalidOperationException)
        {
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.StalePlan,
                "确认前数据库内容已变化，请重新解析并预览。");
        }

        using var transaction = context.Database.BeginTransaction();
        ConfirmedImportResult? stage2Result = null;
        try
        {
            var executor = _executor ?? new ConfirmedImportExecutor(
                utcNow: () => request.OccurredAtUtc);
            stage2Result = executor.Execute(
                request.Contract,
                context,
                request.SnapshotDirectory,
                request.ParsedAtUtc);
            if (!stage2Result.Succeeded)
            {
                transaction.Rollback();
                context.ChangeTracker.Clear();
                return stage2Result;
            }

            var importId = stage2Result.ImportId
                ?? throw new InvalidOperationException("A successful import must have an id.");
            var resolved = Measure("resolve_facts", () => ResolveFacts(
                context,
                request.Contract.Plan,
                frozenFacts,
                importId));
            var productsByCode = resolved.ProductsByCode;
            var coldStartedProductIds = new HashSet<long>();
            var coldStartScopes = context.Products.AsNoTracking()
                         .Where(product => product.LastSeenImportId == importId && product.ExpiryManagementStatus == ExpiryManagementStatus.Managed)
                         .Select(product => new { product.CategoryCode, product.PolicyCode, product.PolicyVersion })
                         .Distinct().OrderBy(product => product.CategoryCode).ThenBy(product => product.PolicyCode).ThenBy(product => product.PolicyVersion)
                         .ToArray();
            Measure("cold_start", () =>
            {
                foreach (var scope in coldStartScopes)
                {
                    var coldStart = _coldStart.Execute(context, new ColdStartScopeBaselineRequest(scope.CategoryCode, scope.PolicyCode!, scope.PolicyVersion!.Value, importId, request.BusinessDate, request.OccurredAtUtc));
                    if (coldStart.Started)
                    {
                        foreach (var productId in context.Products.AsNoTracking().Where(product => product.CategoryCode == scope.CategoryCode && product.PolicyCode == scope.PolicyCode && product.PolicyVersion == scope.PolicyVersion).Select(product => product.Id)) coldStartedProductIds.Add(productId);
                    }
                }
            });
            var eligibleProductIds = CompletedScopeProductIds(context);
            var explicitStocks = request.Contract.Plan.ExplicitProductStocks
                .ToDictionary(stock => stock.ProductCode, StringComparer.Ordinal);
            var eligibleStocks = explicitStocks.Values
                .Where(stock => productsByCode.TryGetValue(stock.ProductCode, out var product) &&
                    eligibleProductIds.Contains(product.Id) && !coldStartedProductIds.Contains(product.Id))
                .ToArray();

            Measure("stock_zero", () =>
            {
                foreach (var stock in eligibleStocks.Where(stock => stock.Quantity == 0).OrderBy(stock => stock.ProductCode, StringComparer.Ordinal))
                {
                    if (!productsByCode.TryGetValue(stock.ProductCode, out var product)) throw new InvalidOperationException($"Product {stock.ProductCode} was not persisted by the import.");

                    context.SaveChanges();
                    _stockZeroLifecycle.Execute(context, new ProductStockZeroRequest(product.Id, request.OccurredAtUtc, SourceImportId: importId));
                    context.ChangeTracker.Clear();
                }
            });

            var zeroProductCodes = eligibleStocks
                .Where(stock => stock.Quantity == 0)
                .Select(stock => stock.ProductCode)
                .ToHashSet(StringComparer.Ordinal);
            var positiveGroups = resolved.BatchFacts.Where(fact => !coldStartedProductIds.Contains(productsByCode[fact.ProductCode].Id))
                .GroupBy(fact => fact.ProductCode, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => TryCreateEligibleGroup(group, productsByCode, eligibleProductIds, zeroProductCodes))
                .Where(group => group is not null)
                .Select(group => group!)
                .ToArray();

            Measure("post_import", () =>
            {
                if (positiveGroups.Length > 0)
                {
                    foreach (var groups in positiveGroups.Chunk(PostImportProductGroupBatchSize))
                    {
                        _postImportLifecycle.Execute(
                            context,
                            new PostImportLifecycleRequest(importId, request.BusinessDate, request.OccurredAtUtc, groups));
                        context.ChangeTracker.Clear();
                    }
                }
            });

            transaction.Commit();
            context.ChangeTracker.Clear();
            return stage2Result;
        }
        catch
        {
            try
            {
                transaction.Rollback();
            }
            catch
            {
            }

            context.ChangeTracker.Clear();
            return ConfirmedImportResult.Fail(
                ConfirmedImportCodes.TransactionFailed,
                "确认导入及生命周期事务失败，数据库写入已回滚。",
                stage2Result?.SnapshotPath,
                stage2Result?.SnapshotMetadata);
        }
    }

    public ConfirmedImportResult Execute(
        StoreDbContext context,
        ImportConfirmationContract contract,
        string snapshotDirectory,
        DateTime parsedAtUtc,
        DateOnly businessDate,
        DateTime occurredAtUtc) => Execute(
            context,
            new ConfirmedImportLifecycleRequest(
                contract,
                snapshotDirectory,
                parsedAtUtc,
                businessDate,
                occurredAtUtc));

    private static void ValidateRequest(ConfirmedImportLifecycleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SnapshotDirectory);
        if (request.ParsedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("ParsedAtUtc must be UTC.", nameof(request));
        }

        if (request.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("OccurredAtUtc must be UTC.", nameof(request));
        }
    }

    private static FrozenImportFacts FreezeFacts(StoreDbContext context, ImportPlan plan)
    {
        var batchPlans = BatchPlans(plan).ToArray();
        var productCodes = batchPlans
            .Select(planItem => planItem.Key.ProductCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var products = productCodes
            .Chunk(SqliteParameterBatchSize)
            .SelectMany(codes => context.Products
                .AsNoTracking()
                .Where(product => codes.Contains(product.ProductCode))
                .Select(product => new ProductSnapshot(product.Id, product.ProductCode))
                .ToArray())
            .ToDictionary(product => product.ProductCode, StringComparer.Ordinal);
        var productsById = products.Values.ToDictionary(product => product.Id, product => product.ProductCode);
        var batches = productsById.Keys
            .Chunk(SqliteParameterBatchSize)
            .SelectMany(ids => context.Batches
                .AsNoTracking()
                .Where(batch => ids.Contains(batch.ProductId))
                .ToArray())
            .ToDictionary(batch => new BatchKey(productsById[batch.ProductId], batch.ProductionDate, batch.ExpiryDate));

        var facts = new List<FrozenBatchFact>(batchPlans.Length);
        foreach (var planItem in batchPlans)
        {
            var hasProduct = products.TryGetValue(planItem.Key.ProductCode, out var product);
            var existingBatch = hasProduct
                ? batches.GetValueOrDefault(planItem.Key)
                : null;

            if (planItem.Kind == PostImportBatchFactKinds.New)
            {
                if (existingBatch is not null)
                {
                    throw new InvalidOperationException("The planned new batch already exists.");
                }

                facts.Add(new FrozenBatchFact(
                    planItem.Key,
                    PostImportBatchFactKinds.New,
                    null,
                    0,
                    0,
                    planItem.CurrentArrivalQty,
                    planItem.MaxArrivalQty));
                continue;
            }

            if (existingBatch is null)
            {
                throw new InvalidOperationException("The planned existing batch does not exist.");
            }

            var currentArrivalQty = existingBatch.CurrentArrivalQty;
            var maxArrivalQty = existingBatch.MaxArrivalQty;
            foreach (var change in planItem.Changes)
            {
                switch (change.FieldName)
                {
                    case "CurrentArrivalQty":
                        currentArrivalQty = (int)change.After!;
                        break;
                    case "MaxArrivalQty":
                        maxArrivalQty = (int)change.After!;
                        break;
                }
            }

            facts.Add(new FrozenBatchFact(
                planItem.Key,
                PostImportBatchFactKinds.Existing,
                existingBatch.Id,
                existingBatch.MaxArrivalQty,
                existingBatch.AttentionVersion,
                currentArrivalQty,
                maxArrivalQty));
        }

        return new FrozenImportFacts(facts);
    }

    private static ResolvedImportFacts ResolveFacts(
        StoreDbContext context,
        ImportPlan plan,
        FrozenImportFacts frozenFacts,
        long importId)
    {
        var productCodes = ProductCodes(plan).ToArray();
        var products = productCodes
            .Chunk(SqliteParameterBatchSize)
            .SelectMany(codes => context.Products
                .AsNoTracking()
                .Where(product => codes.Contains(product.ProductCode))
                .Select(product => new ProductSnapshot(
                    product.Id,
                    product.ProductCode,
                    product.EffectiveStockQty,
                    product.LastSeenImportId))
                .ToArray())
            .ToDictionary(product => product.ProductCode, StringComparer.Ordinal);
        var productsById = products.Values.ToDictionary(product => product.Id, product => product.ProductCode);
        var batches = productsById.Keys
            .Chunk(SqliteParameterBatchSize)
            .SelectMany(ids => context.Batches
                .AsNoTracking()
                .Where(batch => ids.Contains(batch.ProductId))
                .ToArray())
            .ToDictionary(batch => new BatchKey(productsById[batch.ProductId], batch.ProductionDate, batch.ExpiryDate));

        foreach (var stock in plan.ExplicitProductStocks)
        {
            if (!products.TryGetValue(stock.ProductCode, out var product) ||
                product.LastSeenImportId != importId ||
                product.EffectiveStockQty != stock.Quantity)
            {
                throw new InvalidOperationException("The imported product stock fact is stale.");
            }
        }

        var facts = new List<ResolvedBatchFact>(frozenFacts.Batches.Count);
        foreach (var frozen in frozenFacts.Batches)
        {
            if (!products.TryGetValue(frozen.Key.ProductCode, out var product))
            {
                throw new InvalidOperationException(
                    $"Product {frozen.Key.ProductCode} was not persisted by the import.");
            }

            if (product.LastSeenImportId != importId)
            {
                throw new InvalidOperationException("The imported product identity is stale.");
            }

            var batch = batches.GetValueOrDefault(frozen.Key);
            if (batch is null || batch.LastSeenImportId != importId || batch.ProductId != product.Id)
            {
                throw new InvalidOperationException("The imported batch facts are stale.");
            }

            if (frozen.Kind == PostImportBatchFactKinds.Existing &&
                (!frozen.ExistingBatchId.HasValue || batch.Id != frozen.ExistingBatchId.Value))
            {
                throw new InvalidOperationException("The imported batch identity changed.");
            }

            var expectedMaxArrivalQty = Math.Max(
                frozen.PreviousMaxArrivalQty,
                frozen.CurrentArrivalQty);
            if (batch.CurrentArrivalQty != frozen.CurrentArrivalQty ||
                batch.MaxArrivalQty != expectedMaxArrivalQty ||
                (frozen.Kind == PostImportBatchFactKinds.Existing &&
                 batch.MaxArrivalQty != frozen.MaxArrivalQty))
            {
                throw new InvalidOperationException("The imported batch after values changed.");
            }

            facts.Add(new ResolvedBatchFact(
                frozen.Key.ProductCode,
                batch.Id,
                frozen.Kind,
                frozen.PreviousMaxArrivalQty,
                frozen.CurrentArrivalQty,
                frozen.ExpectedAttentionVersion,
                frozen.CurrentArrivalQty > frozen.PreviousMaxArrivalQty
                    ? checked(frozen.ExpectedAttentionVersion + 1)
                    : frozen.ExpectedAttentionVersion));
        }

        return new ResolvedImportFacts(products, facts);
    }

    private static HashSet<long> CompletedScopeProductIds(StoreDbContext context) => context.Products
        .AsNoTracking()
        .Where(product =>
            product.ExpiryManagementStatus == ExpiryManagementStatus.Managed &&
            context.ScopeBaselines.Any(baseline =>
                baseline.IsCompleted &&
                baseline.ScopeKey == product.CategoryCode &&
                baseline.PolicyCode == product.PolicyCode &&
                baseline.PolicyVersion == product.PolicyVersion))
        .Select(product => product.Id)
        .ToHashSet();

    private static PostImportProductGroup? TryCreateEligibleGroup(
        IGrouping<string, ResolvedBatchFact> group,
        IReadOnlyDictionary<string, ProductSnapshot> productsByCode,
        ISet<long> eligibleProductIds,
        ISet<string> zeroProductCodes)
    {
        if (zeroProductCodes.Contains(group.Key) ||
            !productsByCode.TryGetValue(group.Key, out var product) ||
            product.EffectiveStockQty <= 0 ||
            !eligibleProductIds.Contains(product.Id))
        {
            return null;
        }

        return new PostImportProductGroup(product.Id, group.Select(fact => fact.ToPostImportFact()).ToArray());
    }

    private static IEnumerable<PlannedBatch> BatchPlans(ImportPlan plan)
    {
        foreach (var batch in plan.NewBatches)
        {
            yield return new PlannedBatch(
                new BatchKey(batch.BatchKey),
                PostImportBatchFactKinds.New,
                batch.CurrentArrivalQty,
                batch.MaxArrivalQty,
                []);
        }

        foreach (var batch in plan.UpdatedBatches)
        {
            yield return new PlannedBatch(
                new BatchKey(batch.BatchKey),
                PostImportBatchFactKinds.Existing,
                0,
                0,
                batch.FieldChanges);
        }

        foreach (var batch in plan.UnchangedBatches)
        {
            yield return new PlannedBatch(
                new BatchKey(batch.BatchKey),
                PostImportBatchFactKinds.Existing,
                batch.CurrentArrivalQty,
                batch.MaxArrivalQty,
                []);
        }
    }

    private static IEnumerable<string> ProductCodes(ImportPlan plan) => plan.NewProducts
        .Select(product => product.ProductCode)
        .Concat(plan.UpdatedProducts.Select(product => product.ProductCode))
        .Concat(plan.UnchangedProducts.Select(product => product.ProductCode))
        .Concat(plan.NewBatches.Select(batch => batch.BatchKey.ProductCode))
        .Concat(plan.UpdatedBatches.Select(batch => batch.BatchKey.ProductCode))
        .Concat(plan.UnchangedBatches.Select(batch => batch.BatchKey.ProductCode))
        .Concat(plan.ExplicitProductStocks.Select(stock => stock.ProductCode))
        .Distinct(StringComparer.Ordinal);

    private T Measure<T>(string stage, Func<T> action)
    {
        var watch = Stopwatch.StartNew();
        try { return action(); }
        finally { watch.Stop(); _measure?.Invoke(stage, watch.Elapsed); }
    }

    private void Measure(string stage, Action action) => Measure(stage, () => { action(); return 0; });

    private readonly record struct BatchKey(
        string ProductCode,
        DateOnly? ProductionDate,
        DateOnly ExpiryDate)
    {
        public BatchKey(ExcelBatchKey key)
            : this(key.ProductCode, key.ProductionDate, key.ExpiryDate)
        {
        }
    }

    private readonly record struct PlannedBatch(
        BatchKey Key,
        string Kind,
        int CurrentArrivalQty,
        int MaxArrivalQty,
        IReadOnlyList<ImportFieldChange> Changes);

    private readonly record struct ProductSnapshot(
        long Id,
        string ProductCode,
        int EffectiveStockQty = 0,
        long? LastSeenImportId = null);

    private readonly record struct FrozenBatchFact(
        BatchKey Key,
        string Kind,
        long? ExistingBatchId,
        int PreviousMaxArrivalQty,
        int ExpectedAttentionVersion,
        int CurrentArrivalQty,
        int MaxArrivalQty);

    private sealed class FrozenImportFacts
    {
        public FrozenImportFacts(IReadOnlyList<FrozenBatchFact> batches)
        {
            Batches = Array.AsReadOnly(batches.ToArray());
        }

        public IReadOnlyList<FrozenBatchFact> Batches { get; }
    }

    private sealed class ResolvedImportFacts
    {
        public ResolvedImportFacts(
            IReadOnlyDictionary<string, ProductSnapshot> productsByCode,
            IReadOnlyList<ResolvedBatchFact> batchFacts)
        {
            ProductsByCode = productsByCode;
            BatchFacts = Array.AsReadOnly(batchFacts.ToArray());
        }

        public IReadOnlyDictionary<string, ProductSnapshot> ProductsByCode { get; }

        public IReadOnlyList<ResolvedBatchFact> BatchFacts { get; }
    }

    private readonly record struct ResolvedBatchFact(
        string ProductCode,
        long BatchId,
        string Kind,
        int PreviousMaxArrivalQty,
        int CurrentArrivalQty,
        int ExpectedAttentionVersionBefore,
        int TargetAttentionVersionAfter)
    {
        public PostImportBatchFact ToPostImportFact() => new(
            BatchId,
            Kind,
            PreviousMaxArrivalQty,
            CurrentArrivalQty,
            Kind == PostImportBatchFactKinds.Existing
                ? ExpectedAttentionVersionBefore
                : null,
            Kind == PostImportBatchFactKinds.Existing
                ? TargetAttentionVersionAfter
                : null);
    }
}
