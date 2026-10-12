// SPDX-License-Identifier: GPL-2.0-only

using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <c>.oss-scanner/Dockerfile</c> is the image Anthropic's OSS Scanner audits DwarfMapper in, and it repeats facts
    ///     whose source of truth lives elsewhere: the SDK (<c>global.json</c>), and the ilverify version, the restore,
    ///     build and test commands (<c>ci.yml</c>). An image that drifts from them still builds - on another compiler,
    ///     another verifier, another suite - and the audit then reads as covering this repository while it covered
    ///     something next to it. ISSUE-038 was the same drift between <c>global.json</c> and the workflow; this holds
    ///     the third copy to the first two.
    ///     <para>
    ///         <c>.oss-scanner/threat_model.md</c> tells the scanner which tests and paths to read. A name there that no
    ///         longer resolves sends an auditor to nothing, so those are held the way <c>SecurityDocIsCurrentTests</c>
    ///         holds SECURITY.md's citations.
    ///     </para>
    /// </summary>
    public class OssScannerImageTests
    {
        private const string IlVerifyInstall = @"dotnet tool install --global dotnet-ilverify --version (?<v>\S+)";

        private static string Read(params string[] relative)
        {
            var path = Path.Combine([RepoPaths.Root, .. relative]);
            Assert.True(File.Exists(path), $"{string.Join('/', relative)} not found at {path}.");
            return File.ReadAllText(path);
        }

        private static string Dockerfile() => Read(".oss-scanner", "Dockerfile");

        private static string ThreatModel() => Read(".oss-scanner", "threat_model.md");

        private static string Ci() => Read(".github", "workflows", "ci.yml");

        private static string AllTestSources() => string.Join('\n', RepoPaths.SourceFiles(RepoPaths.Tests).Select(File.ReadAllText));

        private static List<string> MissingClasses(IEnumerable<string> names, string sources) =>
            names.Where(c => !Regex.IsMatch(sources, @"\bclass\s+" + Regex.Escape(c) + @"\b")).ToList();

        /// <summary>
        ///     The SDK <c>global.json</c> pins under <paramref name="root" />: the working copy's, or - when a job removed
        ///     it - the commit's. roslyn-forward-compat and preview-sdk-canary run this suite after <c>rm -f global.json</c>
        ///     on purpose, to build on a newer SDK; "the committed pin is untouched", and it is still the pin the image
        ///     must match. A depth-1 checkout has HEAD's tree, so <c>git show</c> reads it there too.
        /// </summary>
        private static string PinnedSdk(string root)
        {
            var path = Path.Combine(root, "global.json");
            var json = File.Exists(path) ? File.ReadAllText(path) : Git(root, "show", "HEAD:global.json");
            return Regex.Match(json, "\"version\"\\s*:\\s*\"(?<v>[^\"]+)\"").Groups["v"].Value;
        }

        private static string Git(string workDir, params string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(workDir);
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var process = new Process();
            process.StartInfo = psi;
            try
            {
                process.Start();
            }
            catch (Win32Exception e)
            {
                Assert.Fail("git is not on PATH - the SDK pin a job removed from the working copy cannot be read from " +
                            "the commit on this machine. " + e.Message);
            }

            // Async reads before the bounded wait, so neither a full pipe nor a hung git defeats the 60 s ceiling.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(true);
                Assert.Fail($"git {string.Join(' ', args)} did not finish within 60 s - killed.");
            }

            Assert.True(process.ExitCode == 0,
                $"git {string.Join(' ', args)} exited {process.ExitCode} in {workDir}: {stderr.GetAwaiter().GetResult()}");
            return stdout.GetAwaiter().GetResult();
        }

        [Fact]
        public void A_job_that_removes_global_json_still_has_the_committed_pin_to_compare()
        {
            // PR #7's roslyn-forward-compat leg: "global.json not found", because that job deletes the working copy's
            // pin to build on a newer SDK. Modelled here: a repository whose commit has the pin and whose working copy
            // does not.
            var repo = Directory.CreateTempSubdirectory("dwarf-sdk-pin-").FullName;
            try
            {
                Git(repo, "-c", "init.defaultBranch=main", "init", "--quiet");
                File.WriteAllText(Path.Combine(repo, "global.json"), "{ \"sdk\": { \"version\": \"9.9.999\", \"rollForward\": \"disable\" } }");
                Git(repo, "add", "global.json");
                Git(repo, "-c", "user.name=pin", "-c", "user.email=pin@example.invalid", "-c", "commit.gpgsign=false",
                    "commit", "--quiet", "--no-verify", "-m", "pin");
                File.Delete(Path.Combine(repo, "global.json"));

                Assert.Equal("9.9.999", PinnedSdk(repo));
            }
            finally
            {
                // git writes its objects read-only, which Directory.Delete refuses on Windows.
                foreach (var f in Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(repo, recursive: true);
            }
        }

        [Fact]
        public void The_image_is_the_SDK_global_json_pins()
        {
            var pinned = PinnedSdk(RepoPaths.Root);
            var from = Regex.Match(Dockerfile(), @"^FROM mcr\.microsoft\.com/dotnet/sdk:(?<tag>[^@\s]+)@sha256:[0-9a-f]{64}\s*$", RegexOptions.Multiline);

            // By tag AND digest: the digest is what Docker resolves, the tag is what can be compared with global.json.
            Assert.True(from.Success, "The image's FROM must be mcr.microsoft.com/dotnet/sdk:<global.json version>@sha256:<digest>.");
            Assert.False(string.IsNullOrEmpty(pinned), "global.json pins no SDK version.");
            Assert.Equal(pinned, from.Groups["tag"].Value);
        }

        [Fact]
        public void The_image_installs_the_ilverify_ci_yml_pins()
        {
            var ci = Regex.Matches(Ci(), IlVerifyInstall).Select(m => m.Groups["v"].Value).Distinct(StringComparer.Ordinal).ToList();
            var image = Regex.Match(Dockerfile(), IlVerifyInstall);

            Assert.True(ci.Count == 1, "ci.yml installs ilverify at more than one version, or not at all: " + string.Join(", ", ci));
            Assert.True(image.Success, "The image does not install ilverify, which EmittedIlIsVerifiableTests shells out to.");
            Assert.Equal(ci[0], image.Groups["v"].Value);
        }

        [Theory]
        [InlineData("dotnet restore DwarfMapper.NET.sln --locked-mode")]
        [InlineData("dotnet build DwarfMapper.NET.sln -c Release --no-restore")]
        public void The_image_restores_and_builds_the_way_CI_does(string command)
        {
            Assert.Contains(command, Ci(), StringComparison.Ordinal);
            Assert.Contains(command, Dockerfile(), StringComparison.Ordinal);
        }

        [Fact]
        public void The_image_runs_CI_s_push_time_test_filter()
        {
            var filter = Regex.Match(Dockerfile(), "dotnet test DwarfMapper\\.NET\\.sln [^\\n]*--filter \"(?<f>[^\"]+)\"").Groups["f"].Value;

            Assert.False(string.IsNullOrEmpty(filter), "The image runs no filtered `dotnet test` over the solution.");
            Assert.Contains("--filter \"" + filter + "\"", Ci(), StringComparison.Ordinal);
        }

        [Fact]
        public void The_audit_runs_online_and_is_off_only_after_it()
        {
            // NuGetAudit needs the network: switched off before the restore, the image would carry no audit at all.
            // `ENV NuGetAudit=false` did nothing - Directory.Build.props' own property outranks it - and a forced
            // restore still failed NU1900 offline; the image declares itself instead, and the props make the exception.
            var dockerfile = Dockerfile();
            var restore = dockerfile.IndexOf("dotnet restore", StringComparison.Ordinal);
            var off = dockerfile.IndexOf("ENV DWARFMAPPER_OFFLINE_IMAGE=true", StringComparison.Ordinal);

            Assert.True(restore >= 0 && off > restore, "ENV DWARFMAPPER_OFFLINE_IMAGE=true must come after the restore it would otherwise disarm.");
            Assert.DoesNotMatch(@"(?m)^\s*ENV\s+NuGetAudit", dockerfile);
        }

        [Fact]
        public void The_props_keep_the_audit_on_with_the_offline_image_as_the_one_exception()
        {
            var props = Read("Directory.Build.props");
            var on = props.IndexOf("<NuGetAudit>true</NuGetAudit>", StringComparison.Ordinal);
            var exception = props.IndexOf("<NuGetAudit Condition=\"'$(DWARFMAPPER_OFFLINE_IMAGE)' == 'true'\">false</NuGetAudit>", StringComparison.Ordinal);

            // After the gate, or the gate would overwrite it; and no other assignment that could switch it off.
            Assert.True(on >= 0 && exception > on, "Directory.Build.props must set NuGetAudit true, then make the offline-image exception.");
            Assert.Equal(2, Regex.Matches(props, @"<NuGetAudit[\s>]").Count);
        }

        [Fact]
        public void No_workflow_or_script_takes_the_exception_or_switches_the_audit_off()
        {
            // The exception exists for the offline image alone. A CI job that took it - or passed NuGetAudit=false,
            // which outranks the props - would turn the supply-chain gate into a vacuous green.
            var bypass = new Regex(@"DWARFMAPPER_OFFLINE_IMAGE|NuGetAudit\s*[=:]\s*['""]?false", RegexOptions.IgnoreCase);
            var files = Directory.GetFiles(Path.Combine(RepoPaths.Root, ".github", "workflows"), "*.yml")
                .Concat(Directory.GetFiles(Path.Combine(RepoPaths.Root, "scripts"), "*.ps1", SearchOption.AllDirectories))
                .ToList();
            var offenders = files.Where(f => bypass.IsMatch(File.ReadAllText(f))).Select(f => Path.GetRelativePath(RepoPaths.Root, f)).ToList();

            Assert.True(files.Count >= 4, $"Only {files.Count} workflow and script files found - the scan is reading the wrong place.");
            Assert.True(offenders.Count == 0, "A workflow or script switches the NuGet audit off:\n  " + string.Join("\n  ", offenders));
            Assert.Matches(bypass, "dotnet restore -p:NuGetAudit=false");
            Assert.Matches(bypass, "env:\n  DWARFMAPPER_OFFLINE_IMAGE: 'true'");
        }

        [Fact]
        public void The_checkout_is_where_the_scanner_contract_puts_it()
        {
            var dockerfile = Dockerfile();

            Assert.Matches(@"(?m)^COPY \. /src\s*$", dockerfile);
            Assert.Matches(@"(?m)^WORKDIR /src\s*$", dockerfile);
        }

        [Fact]
        public void Every_test_class_the_threat_model_names_exists()
        {
            var named = Regex.Matches(ThreatModel(), @"`(?<id>[A-Z]\w*Tests)`").Select(m => m.Groups["id"].Value).Distinct(StringComparer.Ordinal).ToList();
            var missing = MissingClasses(named, AllTestSources());

            Assert.True(named.Count >= 3, $"The threat model names only {named.Count} test classes; it is meant to point the scanner at them.");
            Assert.True(missing.Count == 0, "threat_model.md names a test class that does not exist:\n  " + string.Join("\n  ", missing));
        }

        [Fact]
        public void Every_path_the_threat_model_names_exists()
        {
            var named = Regex.Matches(ThreatModel(), @"`(?<p>(?:src|tests|samples|benchmarks|conformance|scripts|Issues|docs)/[^`\s]*)`")
                .Select(m => m.Groups["p"].Value).Distinct(StringComparer.Ordinal).ToList();
            var missing = named.Where(p => !File.Exists(Path.Combine(RepoPaths.Root, p)) && !Directory.Exists(Path.Combine(RepoPaths.Root, p))).ToList();

            Assert.True(named.Count >= 5, $"The threat model names only {named.Count} repository paths.");
            Assert.True(missing.Count == 0, "threat_model.md names a path that does not exist:\n  " + string.Join("\n  ", missing));
        }

        [Fact]
        public void The_checks_would_catch_a_dead_reference()
        {
            // Control: the class matcher resolves a real class and rejects an invented one, and the ilverify pattern
            // reads a version out of a line shaped like CI's - or the tests above pass whatever the files say.
            var missing = MissingClasses(["DeepRecursionPocTests", "ThisGuardWasDeletedTests"], AllTestSources());

            Assert.Equal(["ThisGuardWasDeletedTests"], missing);
            Assert.Equal("1.2.3", Regex.Match("run: dotnet tool install --global dotnet-ilverify --version 1.2.3", IlVerifyInstall).Groups["v"].Value);
        }
    }
}
