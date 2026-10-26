using System.Globalization;
using System.Text;
using PubgMapDistanceTool.Core.CalibrationModel;
using PubgMapDistanceTool.Core.Measurements;

namespace PubgMapDistanceTool.Core.Export;

public static class CsvExporter
{
    private static readonly string[] Header =
    [
        "测量编号",
        "起点 X 坐标",
        "起点 Y 坐标",
        "终点 X 坐标",
        "终点 Y 坐标",
        "直线距离（米）",
        "水平距离（米）",
        "垂直距离（米）"
    ];

    public static string Build(IEnumerable<MeasurementRecord> records, Calibration calibration)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(calibration);

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', Header));

        foreach (var record in records.OrderBy(item => item.Number))
        {
            var result = record.Calculate(calibration);
            var values = new[]
            {
                record.Number.ToString(CultureInfo.InvariantCulture),
                FormatCoordinate(record.Start.X),
                FormatCoordinate(record.Start.Y),
                FormatCoordinate(record.End.X),
                FormatCoordinate(record.End.Y),
                FormatDistance(result.StraightMeters),
                FormatDistance(result.HorizontalMeters),
                FormatDistance(result.VerticalMeters)
            };

            builder.AppendLine(string.Join(',', values.Select(Escape)));
        }

        return builder.ToString();
    }

    public static void Write(
        string path,
        IEnumerable<MeasurementRecord> records,
        Calibration calibration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, Build(records, calibration), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string FormatCoordinate(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatDistance(double value) =>
        value.ToString("0.0##", CultureInfo.InvariantCulture);

    private static string Escape(string value)
    {
        if (!value.ContainsAny([',', '"', '\r', '\n']))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
