using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using StoreExpiryInspector.Application.Tasks;
using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.UI;

public sealed class InspectionResultImportSessionViewModel : ViewModelBase
{
    private readonly Func<string, InspectionPlanPreview> _preview;
    private readonly Func<ApplyInspectionPlanDraftRequest, ApplyInspectionPlanDraftResult> _apply;
    private readonly Func<BulkInspectionSubmissionRequest, BulkInspectionSubmissionResult> _submit;
    private readonly Func<IReadOnlyCollection<long>, Task> _refresh;
    private readonly Func<IReadOnlyList<OverStockConfirmation>, bool>? _confirmOverStock;
    private readonly Func<ExpiredInventoryWarning, bool>? _confirmExpired;
    private readonly Func<bool>? _confirmSubmission;
    private readonly Action<Exception>? _log;
    private readonly Func<DateOnly> _today;
    private readonly Func<DateTime> _utcNow;
    private InspectionPlanPreview? _result;
    private ApplyInspectionPlanDraftResult? _draft;
    private IReadOnlyList<OverStockConfirmation> _overStock = [];
    private Intent? _intent;
    private bool _busy, _reading;
    private string _status = string.Empty, _inspector = string.Empty, _checkDateText = string.Empty, _inspectorError = string.Empty, _dateError = string.Empty, _filter = "全部";
    private DateTime? _checkDate;

    public InspectionResultImportSessionViewModel(Func<string, InspectionPlanPreview> preview, Func<ApplyInspectionPlanDraftRequest, ApplyInspectionPlanDraftResult> apply,
        Func<BulkInspectionSubmissionRequest, BulkInspectionSubmissionResult> submit, Func<IReadOnlyCollection<long>, Task> refresh,
        Func<IReadOnlyList<OverStockConfirmation>, bool>? confirmOverStock = null, Func<ExpiredInventoryWarning, bool>? confirmExpired = null,
        Func<bool>? confirmSubmission = null, Action<Exception>? log = null, Func<DateOnly>? today = null, Func<DateTime>? utcNow = null)
    {
        _preview = preview; _apply = apply; _submit = submit; _refresh = refresh; _confirmOverStock = confirmOverStock; _confirmExpired = confirmExpired;
        _confirmSubmission = confirmSubmission; _log = log; _today = today ?? (() => DateOnly.FromDateTime(DateTime.Today)); _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _checkDate = _today().ToDateTime(TimeOnly.MinValue);
        _checkDateText = _checkDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        PreviewFilterCommand = new RelayCommand(value => SelectedPreviewFilter = value as string ?? "全部");
        SaveDraftCommand = new RelayCommand(_ => { _ = SaveDraftAsync(); }, _ => CanSaveDraft && !IsActionBusy);
        SubmitCommand = new RelayCommand(_ => { _ = SubmitAsync(); }, _ => CanSaveDraft && !IsActionBusy);
    }

    public ObservableCollection<TodayInspectionPreviewRowViewModel> PreviewRows { get; } = [];
    public IReadOnlyList<string> PreviewFilters { get; } = ["全部", "可提交", "未填写", "状态变化", "填写错误"];
    public RelayCommand PreviewFilterCommand { get; }
    public RelayCommand SaveDraftCommand { get; }
    public RelayCommand SubmitCommand { get; }
    public bool IsActionBusy { get => _busy; private set { if (_busy == value) return; _busy = value; OnPropertyChanged(); Changed(); } }
    public bool IsReadingPlan { get => _reading; private set { if (_reading == value) return; _reading = value; OnPropertyChanged(); } }
    public bool HasPreview => _result is not null;
    public string StatusText { get => _status; private set { if (_status == value) return; _status = value; OnPropertyChanged(); } }
    public string InspectorName { get => _inspector; set { if (_inspector == value) return; _inspector = value; InvalidateDraft(); OnPropertyChanged(); Validate(); } }
    public string CheckDateText
    {
        get => _checkDateText;
        set
        {
            if (_checkDateText == value) return;
            _checkDateText = value;
            _checkDate = DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
            InvalidateDraft();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CheckDateValue));
            Validate();
        }
    }
    public DateTime? CheckDateValue
    {
        get => _checkDate;
        set
        {
            if (_checkDate == value) return;
            _checkDate = value;
            _checkDateText = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            InvalidateDraft();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CheckDateText));
            Validate();
        }
    }
    public DateTime CheckDateMaxValue => _today().ToDateTime(TimeOnly.MinValue);
    public string InspectorNameError { get => _inspectorError; private set { if (_inspectorError == value) return; _inspectorError = value; OnPropertyChanged(); } }
    public string CheckDateError { get => _dateError; private set { if (_dateError == value) return; _dateError = value; OnPropertyChanged(); } }
    public bool HasInspectorNameError => !string.IsNullOrEmpty(InspectorNameError);
    public bool HasCheckDateError => !string.IsNullOrEmpty(CheckDateError);
    public bool IsFormValid => !string.IsNullOrWhiteSpace(InspectorName) && CheckDateValue is DateTime value && DateOnly.FromDateTime(value) <= _today();
    public bool CanSaveDraft => _result?.ApplicableTaskIds.Count > 0 && IsFormValid;
    public bool CanSubmit => CanSaveDraft && !IsActionBusy;
    public IReadOnlyList<long> CompleteTaskIds => _draft?.Tasks.Where(task => task.Readiness.FilledItemCount > 0 && task.Readiness.RequiresReconfirmationCount == 0).Select(task => task.TaskId).ToArray() ?? [];
    public string SelectedPreviewFilter { get => _filter; set { var next = PreviewFilters.Contains(value) ? value : "全部"; if (_filter == next) return; _filter = next; OnPropertyChanged(); OnPropertyChanged(nameof(VisiblePreviewRows)); } }
    public IReadOnlyList<TodayInspectionPreviewRowViewModel> VisiblePreviewRows => SelectedPreviewFilter == "全部" ? PreviewRows : PreviewRows.Where(row => row.ResultText == SelectedPreviewFilter).ToArray();
    public string AllPreviewFilterText => $"全部 {PreviewRows.Count}";
    public string SubmittablePreviewFilterText => $"可提交 {PreviewRows.Count(row => row.ResultText == "可提交")}";
    public string BlankPreviewFilterText => $"未填写 {PreviewRows.Count(row => row.ResultText == "未填写")}";
    public string StalePreviewFilterText => $"状态变化 {PreviewRows.Count(row => row.ResultText == "状态变化")}";
    public string InvalidPreviewFilterText => $"填写错误 {PreviewRows.Count(row => row.ResultText == "填写错误")}";
    public string PreviewSummaryText => _result is null ? "尚未读取排查结果文件" : $"本次共 {_result.Summary.ProductCount} 个商品 / {_result.Summary.BatchCount} 个批次，{PreviewRows.Count(row => row.StatusText == "可提交")} 条可提交";
    public string PreviewIssueText => string.Join("　", new[] { PreviewRows.Count(row => row.StatusText == "未填写") is var blank && blank > 0 ? $"未填写 {blank} 条" : null, PreviewRows.Count(row => row.StatusText == "需要重新导出") is var stale && stale > 0 ? $"状态变化 {stale} 条" : null, PreviewRows.Count(row => row.StatusText == "数据错误") is var invalid && invalid > 0 ? $"填写错误 {invalid} 条" : null, PreviewRows.Count(row => row.StatusText == "可提交") is var valid && valid > 0 ? $"有效 {valid} 条" : null }.Where(value => value is not null));
    public bool HasPreviewIssues => PreviewRows.Any(row => row.HasIssue);
    public string PreviewDetailText => string.Join(Environment.NewLine, PreviewRows.Where(row => row.HasIssue).Select(row => $"第 {row.RowNumber} 行：{row.Reason}"));
    public string DraftStatusText => _draft is null ? "尚未处理排查结果" : CompleteTaskIds.Count > 0 ? "已保存有效排查结果，可以提交；未填写项目会继续待排查。" : "没有可提交的有效排查结果。";
    public string OverStockText => _overStock.Count == 0 ? string.Empty : string.Join("；", _overStock.Select(item => $"{GetPreviewProductName(item.TaskId) ?? $"商品 {item.ProductId}"}：库存 {item.EffectiveStockQty}，本次 {item.TotalCheckedQty}"));
    public event Action<string>? SubmissionBlocked;
    public event Action<string>? PreviewFailed;
    public event Action<IReadOnlyList<long>>? Submitted;
    public string? GetPreviewProductName(long taskId) => _result?.File.Rows.FirstOrDefault(row => row.TaskId == taskId)?.ProductName;

    public async Task PreviewAsync(string path)
    {
        if (IsActionBusy) return;
        Reset(); IsActionBusy = true;
        try
        {
            var reading = Task.Run(() => DatabaseRuntimeGate.Run(() => _preview(path)));
            if (await Task.WhenAny(reading, Task.Delay(180)) != reading) IsReadingPlan = true;
            _result = await reading;
            foreach (var row in _result.File.Rows) { _result.TaskReasons.TryGetValue(row.TaskId ?? 0, out var reason); PreviewRows.Add(new TodayInspectionPreviewRowViewModel(row, reason ?? string.Empty)); }
            StatusText = _result.ApplicableTaskIds.Count == 0 ? "预览完成，但没有可提交的数据。请查看错误或陈旧原因。" : "预览完成，请填写排查人和日期后提交数据。";
            Changed();
        }
        catch (Exception exception) { _log?.Invoke(exception); StatusText = "读取排查结果文件失败"; PreviewFailed?.Invoke(FileMessage(exception)); }
        finally { IsReadingPlan = false; IsActionBusy = false; }
    }

    public Task SaveDraftAsync() => SaveDraftAsync(false);

    private async Task SaveDraftAsync(bool fromSubmit)
    {
        if (IsActionBusy && !fromSubmit) return;
        if (_result is null) { Block("请先读取排查结果文件。", "请先选择并读取已填写的排查计划。"); return; }
        Validate(); if (!IsFormValid || CheckDateValue is not DateTime date) { Block("请完善排查人和排查日期。", string.Join("\n", new[] { InspectorNameError, CheckDateError }.Where(value => !string.IsNullOrEmpty(value)))); return; }
        try { IsActionBusy = true; _draft = await Task.Run(() => DatabaseRuntimeGate.Run(() => _apply(new(_result, _result.ApplicableTaskIds, InspectorName, DateOnly.FromDateTime(date), _today(), RequireUtc())))); StatusText = DraftStatusText; Changed(); }
        catch (Exception exception) { _log?.Invoke(exception); StatusText = "提交数据准备失败，请重新导出最新计划后重试"; }
        finally { IsActionBusy = false; }
    }

    public async Task SubmitAsync()
    {
        if (IsActionBusy) return;
        if (_result is null) { Block("请先读取排查结果文件。", "请先选择并读取已填写的排查计划。"); return; }
        if (_draft is null) await SaveDraftAsync(true);
        if (_draft is null || CompleteTaskIds.Count == 0) { if (IsFormValid) Block("没有可提交的有效排查结果。", "请填写至少一项排查数量后再提交。"); return; }
        Validate();
        if (!IsFormValid || CheckDateValue is not DateTime date) { Block("请完善排查人和排查日期。", string.Join("\n", new[] { InspectorNameError, CheckDateError }.Where(value => !string.IsNullOrEmpty(value)))); return; }
        var expired = ExpiredWarning();
        if (expired is not null && _confirmExpired is not null)
        {
            if (_confirmExpired(expired) != true) { StatusText = "请复核过期商品库存后再提交。"; return; }
        }
        else if (_confirmSubmission?.Invoke() != true) { StatusText = "已取消提交数据。"; return; }
        try { _intent ??= new(CompleteTaskIds, InspectorName, DateOnly.FromDateTime(date), _today(), RequireUtc()); }
        catch (Exception exception) { _log?.Invoke(exception); StatusText = "提交数据失败，请检查当前状态后重试。"; return; }
        IsActionBusy = true;
        try
        {
            while (true)
            {
                var intent = _intent; var result = await Task.Run(() => DatabaseRuntimeGate.Run(() => _submit(new(intent.TaskIds, intent.InspectorName, intent.CheckDate, intent.BusinessDate, intent.SubmittedAtUtc, _overStock, true))));
                if (result.Outcome is BulkInspectionSubmissionOutcome.RequiresOverStockConfirmation or BulkInspectionSubmissionOutcome.OverStockConfirmationStale)
                {
                    _overStock = result.OverStockConfirmations; OnPropertyChanged(nameof(OverStockText));
                    StatusText = result.Outcome == BulkInspectionSubmissionOutcome.OverStockConfirmationStale ? "超库存事实已变化，请重新确认。" : "存在超库存排查项，请返回检查或确认仍然提交。";
                    if (_confirmOverStock?.Invoke(_overStock) == true) continue;
                    _intent = null; _overStock = []; OnPropertyChanged(nameof(OverStockText)); return;
                }
                if (result.Outcome == BulkInspectionSubmissionOutcome.NoValidRows) { StatusText = $"没有有效排查结果已提交；状态变化 {result.Skipped?.Count ?? 0} 条，请重新导出最新计划。"; return; }
                try { await _refresh(intent.TaskIds); StatusText = "提交已成功，相关页面已刷新。"; }
                catch (Exception exception) { _log?.Invoke(exception); StatusText = "提交已成功，部分页面刷新失败，可手动刷新。"; }
                Submitted?.Invoke(intent.TaskIds); Reset(); return;
            }
        }
        catch (Exception exception) { _log?.Invoke(exception); Block("提交数据失败，请检查当前状态后重试。", "请重新导出最新计划并确认数据后再试。"); }
        finally { IsActionBusy = false; }
    }

    public void CancelPreview() { if (_result is not null) Reset(); }
    private ExpiredInventoryWarning? ExpiredWarning() { if (_result is null) return null; var ids = CompleteTaskIds.ToHashSet(); var rows = _result.File.Rows.Where(row => row.Stage == ExpiryStageCalculator.Expired && row.CheckedQty > 0 && row.TaskId is long id && _result.ApplicableTaskIds.Contains(id) && ids.Contains(id)).ToArray(); return rows.Length == 0 ? null : new(rows.Length, rows.Sum(row => (long)row.CheckedQty!.Value)); }
    private void Reset() { _result = null; _draft = null; _intent = null; _overStock = []; PreviewRows.Clear(); _filter = "全部"; OnPropertyChanged(nameof(SelectedPreviewFilter)); Changed(); OnPropertyChanged(nameof(OverStockText)); }
    private void InvalidateDraft() { if (_draft is null) return; _draft = null; _intent = null; _overStock = []; Changed(); OnPropertyChanged(nameof(OverStockText)); }
    private void Validate() { InspectorNameError = string.IsNullOrWhiteSpace(InspectorName) ? "请输入排查人" : string.Empty; CheckDateError = CheckDateValue is null ? "请选择排查日期" : CheckDateValue.Value.Date > CheckDateMaxValue.Date ? "排查日期不能晚于今天" : string.Empty; OnPropertyChanged(nameof(HasInspectorNameError)); OnPropertyChanged(nameof(HasCheckDateError)); Changed(); }
    private void Changed() { foreach (var name in new[] { nameof(HasPreview), nameof(CanSaveDraft), nameof(CanSubmit), nameof(IsFormValid), nameof(CompleteTaskIds), nameof(DraftStatusText), nameof(PreviewSummaryText), nameof(PreviewIssueText), nameof(HasPreviewIssues), nameof(PreviewDetailText), nameof(VisiblePreviewRows), nameof(AllPreviewFilterText), nameof(SubmittablePreviewFilterText), nameof(BlankPreviewFilterText), nameof(StalePreviewFilterText), nameof(InvalidPreviewFilterText) }) OnPropertyChanged(name); SaveDraftCommand.RaiseCanExecuteChanged(); SubmitCommand.RaiseCanExecuteChanged(); }
    private DateTime RequireUtc() { var value = _utcNow(); return value.Kind == DateTimeKind.Utc ? value : throw new InvalidOperationException("权威提交时间必须为 UTC。"); }
    private void Block(string status, string reason) { StatusText = status; SubmissionBlocked?.Invoke(reason); }
    private static string FileMessage(Exception exception) => exception switch { FileNotFoundException => "找不到排查结果文件。请确认文件位置后重新选择。", IOException => "文件暂时无法读取，可能还在 Excel 或 WPS 中打开。请先保存并关闭表格，再重新导入。", InvalidDataException => "排查计划的表格结构已经发生变化或文件已损坏，软件无法安全识别。请重新导出最新排查计划。", _ => "这个 Excel 文件无法正常读取，可能已经损坏。请重新导出排查计划后再试。" };
    private sealed record Intent(IReadOnlyList<long> TaskIds, string InspectorName, DateOnly CheckDate, DateOnly BusinessDate, DateTime SubmittedAtUtc);
}
