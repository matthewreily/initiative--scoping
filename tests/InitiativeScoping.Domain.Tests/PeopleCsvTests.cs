using System.Globalization;
using InitiativeScoping.Application.People;

namespace InitiativeScoping.Domain.Tests;

public class PeopleCsvTests
{
    private const string Header = "DisplayName,ExternalIds,ResourceType,BusinessUnit,Seniority,Location,ResourcingClass";
    private const string Row = "Pat,PAT,Software Engineer,Boarding,Mid,Onshore,Internal";

    [Theory]
    [InlineData("Pods")]
    [InlineData("Pod")]
    [InlineData("Development Pod")]
    [InlineData("Development Pods")]
    public void Parses_optional_pod_header_aliases_and_preserves_explicit_percentages(string header)
    {
        var result = Parse($"{Header},{header}\n{Row}, Delivery : 40.25 ; Platform ; Minimum:1;Maximum:100\n");
        Assert.True(result.IsValid);
        Assert.Equal(new[] { new PeopleCsvPod("Delivery", 40.25m), new("Platform"), new("Minimum", 1), new("Maximum", 100) }, Assert.Single(result.Rows).Pods);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(",Pods\n", ",")]
    [InlineData(",Pods\n", ",   ")]
    public void Missing_or_empty_pods_changes_nothing(string suffix, string cell = "")
    {
        var result = Parse(suffix.Length == 0 ? $"{Header}\n{Row}\n" : $"{Header}{suffix}{Row}{cell}\n");
        Assert.True(result.IsValid);
        Assert.Empty(Assert.Single(result.Rows).Pods!);
    }

    [Theory]
    [InlineData("Pod:0")]
    [InlineData("Pod:100.01")]
    [InlineData("Pod:-1")]
    [InlineData("Pod:NaN")]
    [InlineData("Pod:50%")]
    [InlineData("Pod:")]
    [InlineData(":40")]
    [InlineData("Pod:40:60")]
    [InlineData("Pod;;Other")]
    [InlineData("Pod;")]
    [InlineData("Pod; pod : 50")]
    public void Malformed_pods_are_line_errors(string cell)
    {
        var result = Parse($"{Header},Pods\n{Row},{cell}\n");
        Assert.False(result.IsValid);
        Assert.Equal(2, Assert.Single(result.Errors).Line);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Overlong_pod_names_are_rejected_before_the_database_write()
    {
        Assert.False(Parse($"{Header},Pods\n{Row},{new string('P', 201)}\n").IsValid);
    }

    [Fact]
    public void Writer_round_trips_memberships_and_csv_quoting()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        PeopleCsv.Write(writer, [new PeopleCsvRow("Pat", ["PAT"], "Engineer", "Boarding", "Mid", "Onshore", TestClasses.Internal, true,
            Pods: [new("Operations, East", 12.5m), new("Platform")])]);
        var result = Parse(writer.ToString());
        Assert.True(result.IsValid);
        Assert.Equal(new[] { new PeopleCsvPod("Operations, East", 12.5m), new("Platform") }, Assert.Single(result.Rows).Pods);
    }

    private static PeopleCsvResult Parse(string csv) => PeopleCsv.Parse(new StringReader(csv), [TestClasses.Internal]);
}
