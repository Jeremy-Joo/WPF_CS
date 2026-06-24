namespace TekkenStats.Core;

/// <summary>컬럼명 + 행(object 배열). 집계 결과이자 엑셀 출력 단위.</summary>
public sealed class Table
{
    public List<string> Columns { get; } = new();
    public List<object?[]> Rows { get; } = new();

    public Table(params string[] cols) => Columns.AddRange(cols);
    public void Add(params object?[] row) => Rows.Add(row);
    public int Count => Rows.Count;

    /// <summary>컬럼명(정규화 전)을 찾아 해당 열을 제거.</summary>
    public void RemoveColumn(string col)
    {
        int i = Columns.IndexOf(col);
        if (i < 0) return;
        Columns.RemoveAt(i);
        for (int r = 0; r < Rows.Count; r++)
        {
            var list = Rows[r].ToList();
            if (i < list.Count) list.RemoveAt(i);
            Rows[r] = list.ToArray();
        }
    }
}
