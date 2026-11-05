using System.Reflection;
using System.Text.RegularExpressions;

namespace CentralGit.Architecture.Tests;

/// <summary>
/// The dependency rule: Domain ← Application ← Infrastructure, and the App only reaches
/// Infrastructure from its composition root. Checked on the compiled assemblies (references)
/// and, for the WinUI app that a net10.0 test can't load, on its sources.
/// </summary>
public partial class DependencyRuleTests
{
    private static readonly Assembly Domain = typeof(Domain.Commits.CommitInfo).Assembly;
    private static readonly Assembly Application = typeof(Application.Git.GitResult).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.Git.GitRunner).Assembly;

    private static IReadOnlyList<string> CentralGitReferences(Assembly assembly) =>
        assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("CentralGit", StringComparison.Ordinal))
            .Order()
            .ToList();

    [Fact]
    public void Domain_depends_on_nothing() => Assert.Empty(CentralGitReferences(Domain));

    [Fact]
    public void Domain_uses_only_the_base_library()
    {
        var foreign = Domain
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => !(n.StartsWith("System.", StringComparison.Ordinal) || n is "netstandard"));
        Assert.Empty(foreign);
    }

    [Fact]
    public void Application_depends_only_on_the_domain() =>
        Assert.Equal(["CentralGit.Domain"], CentralGitReferences(Application));

    [Fact]
    public void Infrastructure_implements_the_application() =>
        Assert.Equal(["CentralGit.Application", "CentralGit.Domain"], CentralGitReferences(Infrastructure));

    [Fact]
    public void Domain_and_application_do_no_io_of_their_own()
    {
        string[] forbidden =
        [
            "System.Diagnostics.Process",
            "System.IO.File",
            "System.IO.Directory",
            "System.IO.FileInfo",
            "System.Net.Http.HttpClient",
        ];
        foreach (var assembly in new[] { Domain, Application })
        {
            var used = ReferencedMembers(assembly).Where(name => forbidden.Contains(name)).Distinct();
            Assert.Empty(used);
        }
    }

    [Fact]
    public void The_io_check_can_see_io() // guards the test above against passing vacuously
    {
        var infrastructure = ReferencedMembers(Infrastructure).ToHashSet();
        Assert.Contains("System.IO.File", infrastructure);
        Assert.Contains("System.Diagnostics.Process", infrastructure);
    }

    [Fact]
    public void The_app_reaches_infrastructure_only_from_its_composition_root()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "CentralGit.App");
        var offenders = Directory
            .EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            )
            .Where(f => Path.GetFileName(f) != "App.xaml.cs")
            .Where(f => InfrastructureUsage().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(app, f));
        Assert.Empty(offenders);
    }

    [Fact]
    public void View_models_do_not_run_git_themselves()
    {
        var viewModels = Path.Combine(RepositoryRoot(), "src", "CentralGit.App", "ViewModels");
        var offenders = Directory
            .EnumerateFiles(viewModels, "*.cs")
            .Where(f => ProcessOrFileUsage().IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName);
        Assert.Empty(offenders);
    }

    [GeneratedRegex(@"\bCentralGit\.Infrastructure\b")]
    private static partial Regex InfrastructureUsage();

    [GeneratedRegex(
        @"\b(Process\.Start|ProcessStartInfo|File\.(Read|Write|Exists)|Directory\.(Enumerate|Exists|Create))"
    )]
    private static partial Regex ProcessOrFileUsage();

    /// <summary>Declaring types of every member the assembly references (its MemberRef metadata table).</summary>
    private static IEnumerable<string> ReferencedMembers(Assembly assembly)
    {
        var module = assembly.ManifestModule;
        for (var row = 1; ; row++)
        {
            string? name;
            try
            {
                name = module.ResolveMember(0x0A000000 | row)?.DeclaringType?.FullName;
            }
            catch (ArgumentOutOfRangeException)
            {
                yield break; // past the last row
            }
            catch (ArgumentException)
            {
                continue; // a generic member that needs its context to resolve
            }
            if (name is not null)
                yield return name;
        }
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CentralGit.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException("CentralGit.slnx not found above the test output.");
    }
}
