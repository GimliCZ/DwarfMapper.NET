// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;

namespace DwarfMapper.Unions
{
    /// <summary>
    ///     Runs every example and checks its result, so the program is also a test: the net11 job runs it from the
    ///     packed package and fails on a wrong value or a missing line.
    /// </summary>
    public static class Program
    {
        public static int Main()
        {
            var examples = new List<(string Title, Func<string> Run, string Expected)>
            {
                ("01 union to union", Ex01.Example.Run, Ex01.Example.Expected),
                ("02 explicit arms", Ex02.Example.Run, Ex02.Example.Expected),
                ("03 value cases", Ex03.Example.Run, Ex03.Example.Expected),
                ("04 union members", Ex04.Example.Run, Ex04.Example.Expected),
                ("05 the empty union", Ex05.Example.Run, Ex05.Example.Expected),
            };

            var failures = 0;
            foreach (var (title, run, expected) in examples)
            {
                var actual = run();
                var verdict = string.Equals(actual, expected, StringComparison.Ordinal) ? "ok" : "WRONG";
                Console.WriteLine($"DWARF-UNION-SAMPLE {verdict} {title}: {actual}");
                if (verdict != "ok")
                {
                    Console.WriteLine($"    expected: {expected}");
                    failures++;
                }
            }

            return failures;
        }
    }
}
