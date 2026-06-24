using NPOI.SS.UserModel;
using NPOI.SS.UserModel.Charts;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace TekkenStats.Core;

/// <summary>NPOI 로 Table 들을 xlsx 로 쓴다 (파이썬 openpyxl 출력 포팅).</summary>
public static class ExcelWriter
{
    public static string SafeSheetName(string name)
    {
        foreach (var c in new[] { ':', '\\', '/', '?', '*', '[', ']' })
            name = name.Replace(c, '_');
        return name.Length > 31 ? name[..31] : name;
    }

    private static ICellStyle WrapStyle(IWorkbook wb)
    {
        var s = wb.CreateCellStyle();
        s.WrapText = true;
        s.VerticalAlignment = VerticalAlignment.Center;
        return s;
    }

    private static void SetCell(ICell cell, object? val)
    {
        switch (val)
        {
            case null:
                break;
            case int i: cell.SetCellValue(i); break;
            case long l: cell.SetCellValue(l); break;
            case double d: cell.SetCellValue(d); break;
            case float f: cell.SetCellValue(f); break;
            case bool b: cell.SetCellValue(b); break;
            default: cell.SetCellValue(val.ToString()); break;
        }
    }

    /// <summary>startRow(0-based)부터 헤더 + 데이터 작성.</summary>
    public static void WriteTable(IWorkbook wb, ISheet sheet, Table table, int startRow = 0,
        ICellStyle? headerStyle = null)
    {
        headerStyle ??= WrapStyle(wb);
        var hr = sheet.CreateRow(startRow);
        for (int c = 0; c < table.Columns.Count; c++)
        {
            var cell = hr.CreateCell(c);
            cell.SetCellValue(table.Columns[c]);
            cell.CellStyle = headerStyle;
        }
        for (int r = 0; r < table.Rows.Count; r++)
        {
            var row = sheet.CreateRow(startRow + 1 + r);
            var data = table.Rows[r];
            for (int c = 0; c < data.Length; c++)
                SetCell(row.CreateCell(c), data[c]);
        }
    }

    /// <summary>sheet 의 (top,left)에 제목 + 표를 써넣고 다음 표 시작행 반환.</summary>
    public static int PlaceTable(ISheet sheet, int top, int left, string title, Table table)
    {
        GetOrCreateCell(sheet, top, left).SetCellValue(title);
        for (int j = 0; j < table.Columns.Count; j++)
            GetOrCreateCell(sheet, top + 1, left + j).SetCellValue(table.Columns[j]);
        for (int i = 0; i < table.Rows.Count; i++)
        {
            var data = table.Rows[i];
            for (int j = 0; j < data.Length; j++)
                SetCell(GetOrCreateCell(sheet, top + 2 + i, left + j), data[j]);
        }
        return top + 2 + table.Rows.Count + 2;
    }

    private static ICell GetOrCreateCell(ISheet sheet, int r, int c)
    {
        var row = sheet.GetRow(r) ?? sheet.CreateRow(r);
        return row.GetCell(c) ?? row.CreateCell(c);
    }

    private static int DisplayWidth(string s)
    {
        int max = 0;
        foreach (var line in s.Split('\n'))
        {
            int w = 0;
            foreach (var ch in line) w += ch > 0x7F ? 2 : 1;  // 한글 등 전각 = 2
            if (w > max) max = w;
        }
        return max;
    }

    /// <summary>열 너비 자동맞춤 + 2줄(\n) 헤더 줄바꿈/행높이.</summary>
    public static void AutoFit(IWorkbook wb, ISheet sheet, int min = 8, int max = 60, int pad = 2)
    {
        var widths = new Dictionary<int, int>();
        var rowLines = new Dictionary<int, int>();
        ICellStyle? wrap = null;

        for (int r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            for (int c = row.FirstCellNum; c < row.LastCellNum; c++)
            {
                var cell = row.GetCell(c);
                if (cell == null) continue;
                string s = cell.CellType == CellType.Numeric
                    ? cell.NumericCellValue.ToString()
                    : (cell.StringCellValue ?? "");
                if (s.Length == 0) continue;

                int w = DisplayWidth(s);
                if (!widths.TryGetValue(c, out var cur) || w > cur) widths[c] = w;

                if (s.Contains('\n'))
                {
                    wrap ??= WrapStyle(wb);
                    cell.CellStyle = wrap;
                    int lines = s.Count(ch => ch == '\n') + 1;
                    if (!rowLines.TryGetValue(r, out var rl) || lines > rl) rowLines[r] = lines;
                }
            }
        }

        foreach (var (c, w) in widths)
        {
            int target = Math.Min(max, Math.Max(min, w + pad));
            sheet.SetColumnWidth(c, target * 256);
        }
        foreach (var (r, lines) in rowLines)
        {
            var row = sheet.GetRow(r);
            if (row != null) row.HeightInPoints = 15 * lines + 2;
        }
    }

    /// <summary>power_trend 시트에 캐릭터별 라인 차트 삽입(차트 미지원 환경이면 조용히 건너뜀).</summary>
    public static void AddLineChart(ISheet sheet, int nDataRows, int firstCharCol, int charCount)
    {
        if (nDataRows <= 0 || charCount <= 0) return;
        try
        {
            var drawing = sheet.CreateDrawingPatriarch();
            int anchorCol = firstCharCol + charCount + 1;
            var anchor = drawing.CreateAnchor(0, 0, 0, 0, anchorCol, 1, anchorCol + 12, 24);
            var chart = drawing.CreateChart(anchor);
            // 주의: NPOI 의 범례(legendPos) 직렬화가 잘못된 XML 을 만들어 파일이 깨짐 → 범례 생략

            var data = chart.ChartDataFactory.CreateLineChartData<string, double>();
            var bottom = chart.ChartAxisFactory.CreateCategoryAxis(AxisPosition.Bottom);
            var left = chart.ChartAxisFactory.CreateValueAxis(AxisPosition.Left);
            left.Crosses = AxisCrosses.AutoZero;

            // 카테고리 = dt 열(A=0), 데이터행 1..nDataRows
            var cats = DataSources.FromStringCellRange(sheet, new CellRangeAddress(1, nDataRows, 0, 0));
            for (int i = 0; i < charCount; i++)
            {
                int col = firstCharCol + i;
                var vals = DataSources.FromNumericCellRange(sheet, new CellRangeAddress(1, nDataRows, col, col));
                data.AddSeries(cats, vals);
            }
            chart.Plot(data, bottom, left);
        }
        catch { /* NPOI 차트 미지원/오류 → 데이터 시트는 유지 */ }
    }

    /// <summary>기존 파일/잠김이면 ' (2)' 새 이름으로 저장. 실제 경로 반환.</summary>
    public static string SaveWithFallback(string basePath, Action<IWorkbook> build)
    {
        string dir = Path.GetDirectoryName(basePath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(basePath);
        string ext = Path.GetExtension(basePath);
        Directory.CreateDirectory(dir);

        for (int i = 0; i < 100; i++)
        {
            string cand = i == 0 ? basePath : Path.Combine(dir, $"{name} ({i + 1}){ext}");
            if (File.Exists(cand)) continue;
            try
            {
                using var wb = new XSSFWorkbook();
                build(wb);
                using var fs = new FileStream(cand, FileMode.CreateNew, FileAccess.Write);
                wb.Write(fs);
                return cand;
            }
            catch (IOException) { /* 잠김/충돌 → 다음 이름 */ }
        }
        throw new IOException($"엑셀 저장 실패: {basePath}");
    }
}
