using System.Globalization;
using System.Text;

namespace Inventory.Domain.Services;

public static class VietnameseMoneyTextConverter
{
    private static readonly string[] NumberWords =
    [
        "không",
        "một",
        "hai",
        "ba",
        "bốn",
        "năm",
        "sáu",
        "bảy",
        "tám",
        "chín"
    ];

    private static readonly string[] ScaleWords =
    [
        string.Empty,
        "nghìn",
        "triệu",
        "tỷ",
        "nghìn tỷ",
        "triệu tỷ"
    ];

    public static string Convert(decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Số tiền phải lớn hơn hoặc bằng 0.");
        }

        var rounded = decimal.Round(amount, 0, MidpointRounding.AwayFromZero);
        if (rounded == 0)
        {
            return "Không đồng";
        }

        var number = (long)rounded;
        var chunks = new List<int>();
        while (number > 0)
        {
            chunks.Add((int)(number % 1000));
            number /= 1000;
        }

        var result = new StringBuilder();
        for (var i = chunks.Count - 1; i >= 0; i--)
        {
            var chunk = chunks[i];
            if (chunk == 0)
            {
                continue;
            }

            var isFull = i < chunks.Count - 1;
            var chunkText = ReadThreeDigits(chunk, isFull);
            if (chunkText.Length == 0)
            {
                continue;
            }

            if (result.Length > 0)
            {
                result.Append(", ");
            }

            result.Append(chunkText);
            if (!string.IsNullOrEmpty(ScaleWords[i]))
            {
                result.Append(' ').Append(ScaleWords[i]);
            }
        }

        var normalized = result.ToString().Trim();
        var culture = new CultureInfo("vi-VN");
        var firstChar = char.ToUpper(normalized[0], culture);
        return $"{firstChar}{normalized[1..]} đồng";
    }

    private static string ReadThreeDigits(int number, bool isFull)
    {
        var hundred = number / 100;
        var ten = (number % 100) / 10;
        var unit = number % 10;
        var result = new StringBuilder();

        if (hundred > 0 || isFull)
        {
            if (hundred > 0)
            {
                result.Append(NumberWords[hundred]).Append(" trăm");
            }
            else
            {
                result.Append("không trăm");
            }
        }

        if (ten > 1)
        {
            if (result.Length > 0)
            {
                result.Append(' ');
            }

            result.Append(NumberWords[ten]).Append(" mươi");
            if (unit == 1)
            {
                result.Append(" mốt");
            }
            else if (unit == 4)
            {
                result.Append(" tư");
            }
            else if (unit == 5)
            {
                result.Append(" lăm");
            }
            else if (unit > 0)
            {
                result.Append(' ').Append(NumberWords[unit]);
            }
        }
        else if (ten == 1)
        {
            if (result.Length > 0)
            {
                result.Append(' ');
            }

            result.Append("mười");
            if (unit == 5)
            {
                result.Append(" lăm");
            }
            else if (unit > 0)
            {
                result.Append(' ').Append(NumberWords[unit]);
            }
        }
        else
        {
            if (unit > 0)
            {
                if (result.Length > 0)
                {
                    result.Append(" lẻ ");
                }

                result.Append(unit == 5 && hundred > 0 ? "năm" : NumberWords[unit]);
            }
        }

        return result.ToString().Trim();
    }
}
