using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StoreExpiryInspector.Application.Tasks;
using Xunit;

namespace StoreExpiryInspector.Tests;

// S19 replaced private A:Y snapshots with the A:L business contract.
public sealed class V1F03I02InspectionPlanDraftApplyTests
{
    [Fact]
    public void ReaderDistinguishesBlankZeroInvalidAndDuplicateBusinessRows()
    {
        var path = CreatePlan(["", "0", "1.5", "2"]);
        try
        {
            var rows = new InspectionPlanResultReader().Read(path).Rows;
            Assert.Null(rows[0].CheckedQty);
            Assert.Equal(0, rows[1].CheckedQty);
            Assert.Contains(rows[2].Errors, value => value.Contains("排查数量"));
            Assert.Contains(rows[3].Errors, value => value.Contains("重复"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReaderRejectsBrokenBusinessStructureAtFileLevel()
    {
        var path = CreatePlan(["1"], "错误表名");
        try { Assert.Throws<InvalidDataException>(() => new InspectionPlanResultReader().Read(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReaderAcceptsAnArbitrarilyRenamedWorkbook()
    {
        var original = CreatePlan(["1"]);
        var renamed = Path.Combine(Path.GetTempPath(), $"门店随意改名-{Guid.NewGuid():N}.xlsx");
        try
        {
            File.Move(original, renamed);
            var row = new InspectionPlanResultReader().Read(renamed).Rows.Single();
            Assert.Equal("P-1", row.ProductCode);
            Assert.Equal(1, row.CheckedQty);
        }
        finally { if (File.Exists(original)) File.Delete(original); if (File.Exists(renamed)) File.Delete(renamed); }
    }

    private static string CreatePlan(string[] quantities, string sheetName = "今日排查计划")
    {
        var path = Path.Combine(Path.GetTempPath(), $"s19-{Guid.NewGuid():N}.xlsx");
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart(); workbook.Workbook = new Workbook();
        var worksheet = workbook.AddNewPart<WorksheetPart>(); var data = new SheetData();
        var headers = new[] { "序号", "商品编码", "条码", "商品名称", "大类", "生产日期", "有效日期", "当前阶段", "当前批次累计到货", "历史累计到货最大值", "总库存", "本次排查数量" };
        data.Append(new Row(headers.Select((value, index) => Cell(index + 1, 1, value))));
        for (var index = 0; index < quantities.Length; index++) data.Append(new Row(new[] { "1", "P-1", "", "商品", "食品", "2026-01-01", "2026-12-31", "5折", "1", "1", "1", quantities[index] }.Select((value, column) => Cell(column + 1, (uint)index + 2, value))));
        worksheet.Worksheet = new Worksheet(data); workbook.Workbook.Append(new Sheets(new Sheet { Id = workbook.GetIdOfPart(worksheet), SheetId = 1, Name = sheetName })); workbook.Workbook.Save(); return path;
    }
    private static Cell Cell(int column, uint row, string value) => new() { CellReference = $"{(char)('A' + column - 1)}{row}", DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value)) };
}
