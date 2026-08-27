using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using StoreExpiryInspector.Domain;
using StoreExpiryInspector.Infrastructure;

namespace StoreExpiryInspector.Application;

public sealed record ManualInventoryAdjustmentRequest(
    long ProductId,
    int CorrectedStockQty,
    bool ConfirmProductTermination,
    DateTime AdjustedAtUtc);

public sealed record ManualInventoryAdjustmentResult(
    bool Changed,
    long? AdjustmentId,
    int PreviousEffectiveStockQty,
    int CorrectedStockQty,
    bool ProductTerminated)
{
    public bool NoChange => !Changed;
}

public sealed class ManualInventoryAdjustmentUseCase
{
    private readonly ProductStockZeroLifecycleUseCase _stockZeroLifecycle = new();

    public ManualInventoryAdjustmentResult Execute(
        StoreDbContext context,
        ManualInventoryAdjustmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        EnsureCleanContext(context);

        var ownsTransaction = context.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        try
        {
            if (ownsTransaction)
            {
                transaction = context.Database.BeginTransaction();
            }

            context.ChangeTracker.Clear();
            var product = context.Products
                .AsTracking()
                .SingleOrDefault(candidate => candidate.Id == request.ProductId);
            if (product is null)
            {
                throw new KeyNotFoundException($"Product {request.ProductId} does not exist.");
            }

            var previousEffectiveStockQty = product.EffectiveStockQty;
            if (request.CorrectedStockQty == previousEffectiveStockQty)
            {
                transaction?.Commit();
                return new(
                    Changed: false,
                    AdjustmentId: null,
                    PreviousEffectiveStockQty: previousEffectiveStockQty,
                    CorrectedStockQty: request.CorrectedStockQty,
                    ProductTerminated: false);
            }

            var adjustment = new InventoryAdjustment
            {
                ProductId = product.Id,
                ExcelStockQtySnapshot = product.ExcelStockQty,
                AdjustedStockQty = request.CorrectedStockQty,
                AdjustedAtUtc = request.AdjustedAtUtc
            };
            context.InventoryAdjustments.Add(adjustment);
            product.EffectiveStockQty = request.CorrectedStockQty;
            product.EffectiveStockSource = "manual";
            product.UpdatedAtUtc = request.AdjustedAtUtc;
            context.SaveChanges();

            var productTerminated = false;
            if (request.CorrectedStockQty == 0)
            {
                // S3-T04 owns every product-zero batch/task/draft/event transition.
                var lifecycleResult = _stockZeroLifecycle.Execute(
                    context,
                    new ProductStockZeroRequest(
                        product.Id,
                        request.AdjustedAtUtc,
                        SourceAdjustmentId: adjustment.Id));
                productTerminated = lifecycleResult.ProductTerminated;
            }

            transaction?.Commit();
            return new(
                Changed: true,
                AdjustmentId: adjustment.Id,
                PreviousEffectiveStockQty: previousEffectiveStockQty,
                CorrectedStockQty: request.CorrectedStockQty,
                ProductTerminated: productTerminated);
        }
        catch
        {
            if (ownsTransaction)
            {
                try
                {
                    transaction?.Rollback();
                }
                catch
                {
                }
            }

            // The caller owns an outer transaction; it must decide whether to commit or roll back.
            // Clearing our tracked graph prevents uncommitted values from masquerading as success.
            context.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private static void ValidateRequest(ManualInventoryAdjustmentRequest request)
    {
        if (request.ProductId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ProductId));
        }

        if (request.CorrectedStockQty < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.CorrectedStockQty));
        }

        if (request.AdjustedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "AdjustedAtUtc must be UTC.",
                nameof(request.AdjustedAtUtc));
        }

        if (request.CorrectedStockQty == 0 && !request.ConfirmProductTermination)
        {
            throw new ArgumentException(
                "ConfirmProductTermination must be true when correcting stock to zero.",
                nameof(request.ConfirmProductTermination));
        }
    }

    private static void EnsureCleanContext(StoreDbContext context)
    {
        if (context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                "StoreDbContext must have no pending changes before an inventory adjustment.");
        }
    }
}
