namespace ChisBallot.Tests;

using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// The rules in docs/migrations.md, enforced. Every release must work with the
/// database of the release before it, so a rollback of the programs never needs a
/// rollback of the schema. A script that has to break a rule says so on a line
/// "-- breaking-change-approved: reason", which a reviewer will see.
/// </summary>
public sealed partial class MigrationRulesTests
{
    private const string Approval = "-- breaking-change-approved:";

    private static readonly (string Rule, Regex Pattern)[] Forbidden =
    [
        ("drops something an earlier release may still use", DropPattern()),
        ("renames something an earlier release may still use", RenamePattern()),
        ("changes a column's type or makes it stricter", AlterColumnPattern()),
        ("adds a NOT NULL column with no default", NotNullWithoutDefaultPattern()),
        ("empties a table", TruncatePattern()),
        ("deletes or rewrites ballots, codes or selections", BallotDataPattern()),
    ];

    [Fact]
    public void Every_migration_follows_the_rules_or_says_why_not()
    {
        List<string> problems = [];
        foreach ((string name, string sql) in Scripts())
        {
            if (sql.Contains(Approval, StringComparison.Ordinal))
            {
                continue;
            }

            // What a script does to data when it runs, not what the functions it
            // defines do later (cast_ballot updates codes, as it must).
            string code = FunctionDefinition().Replace(LineComment().Replace(sql, string.Empty), string.Empty);
            foreach ((string rule, Regex pattern) in Forbidden)
            {
                Match match = pattern.Match(code);
                if (match.Success)
                {
                    problems.Add($"{name}: {rule}: \"{match.Value.Trim()}\"");
                }
            }
        }

        Assert.True(problems.Count == 0, "See docs/migrations.md.\n" + string.Join('\n', problems));
    }

    [Fact]
    public void The_scripts_are_found_and_numbered_in_order()
    {
        List<string> names = [.. Scripts().Select(script => script.Name)];
        Assert.NotEmpty(names);
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.All(names, name => Assert.Matches(@"^\d{3}_.+\.sql$", name));
    }

    private static IEnumerable<(string Name, string Sql)> Scripts()
    {
        Assembly assembly = typeof(ChisBallot.Data.Migrator).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames().Where(r => r.EndsWith(".sql", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using StreamReader reader = new(assembly.GetManifestResourceStream(resource)!);
            string name = resource[(resource.LastIndexOf(".Migrations.", StringComparison.Ordinal) + ".Migrations.".Length)..];
            yield return (name, reader.ReadToEnd());
        }
    }

    [GeneratedRegex(@"CREATE\s+(OR\s+REPLACE\s+)?FUNCTION.*?\$\$.*?\$\$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FunctionDefinition();

    [GeneratedRegex(@"--[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"\bDROP\s+(TABLE|COLUMN|SCHEMA|FUNCTION|TRIGGER|INDEX|VIEW|ROLE|CONSTRAINT)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DropPattern();

    [GeneratedRegex(@"\bRENAME\b", RegexOptions.IgnoreCase)]
    private static partial Regex RenamePattern();

    [GeneratedRegex(@"\bALTER\s+COLUMN\s+\w+\s+(TYPE|SET\s+NOT\s+NULL)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AlterColumnPattern();

    [GeneratedRegex(@"\bADD\s+COLUMN\b(?![^;,]*\bDEFAULT\b)[^;,]*\bNOT\s+NULL\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotNullWithoutDefaultPattern();

    [GeneratedRegex(@"\bTRUNCATE\b", RegexOptions.IgnoreCase)]
    private static partial Regex TruncatePattern();

    [GeneratedRegex(@"\b(DELETE\s+FROM|UPDATE|INSERT\s+INTO)\s+ballot\.(ballots|voting_codes|ballot_selections|ballot_write_ins)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BallotDataPattern();
}
