// SPDX-License-Identifier: GPL-2.0-only

// 03 - Value-type cases. An int case is not a long case, so the pair is named - with the typeof form, because the
// generic [MapDerivedType<,>] is for classes - and the int is widened on its way into the target union. The string
// case is in both unions and passes through.

namespace DwarfMapper.Unions.Ex03
{
    public union Reading(int, string);

    public union ReadingDto(long, string);

// <snippet: union-value-cases>
    [DwarfMapper]
    public partial class ReadingMapper
    {
        [MapDerivedType(typeof(int), typeof(long))]
        public partial ReadingDto ToDto(Reading reading);
    }
// </snippet>

    public static class Example
    {
        public static string Run()
        {
            var mapper = new ReadingMapper();
            var number = mapper.ToDto(new Reading(42)).Value;
            var text = mapper.ToDto(new Reading("n/a")).Value;
            return number + " (" + number!.GetType().Name + ") | " + text;
        }

        public const string Expected = "42 (Int64) | n/a";
    }
}
