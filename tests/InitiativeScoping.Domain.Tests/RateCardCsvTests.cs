using InitiativeScoping.Application.RateCards;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Tests;

public class RateCardCsvTests
{
    private const string Header = "ResourceType,BusinessUnit,Seniority,Location,ResourcingClass,HourlyRate\n";

    [Fact]
    public void Parses_valid_rows_with_class_aliases_and_trimming()
    {
        var result = RateCardCsv.Parse(new StringReader(Header +
            " Software Engineer , Boarding , senior , Onshore , Internal , 120.50\n" +
            "QA Analyst,Boarding,Mid,Offshore,Contractor,$55\n"));

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(new RateCardCsvRow("Software Engineer", "Boarding", Seniority.Senior, "Onshore", ResourcingClass.InternalFte, 120.50m), result.Rows[0]);
        Assert.Equal(ResourcingClass.Vendor, result.Rows[1].ResourcingClass);
        Assert.Equal(55m, result.Rows[1].HourlyRate);
    }

    [Fact]
    public void Reports_missing_columns()
    {
        var result = RateCardCsv.Parse(new StringReader("ResourceType,BusinessUnit\nA,B\n"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("Missing column"));
    }

    [Fact]
    public void Reports_invalid_values_with_line_numbers()
    {
        var result = RateCardCsv.Parse(new StringReader(Header +
            "SE,Boarding,Guru,Onshore,Internal,100\n" +
            "SE,Boarding,Senior,Onshore,Freelance,100\n" +
            "SE,Boarding,Senior,Onshore,Internal,-5\n" +
            ",Boarding,Senior,Onshore,Internal,100\n"));

        Assert.False(result.IsValid);
        Assert.Empty(result.Rows);
        Assert.Equal([2, 3, 4, 5], result.Errors.Select(e => e.Line).ToArray());
    }

    [Fact]
    public void Detects_duplicate_dimension_tuples_case_insensitively()
    {
        var result = RateCardCsv.Parse(new StringReader(Header +
            "SE,Boarding,Senior,Onshore,Internal,100\n" +
            "se,boarding,SENIOR,onshore,Fte,110\n"));

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Duplicate", result.Errors[0].Message);
    }

    [Fact]
    public void Write_then_parse_round_trips()
    {
        var rows = new[]
        {
            new RateCardCsvRow("Software Engineer", "Boarding", Seniority.Staff, "Onshore", ResourcingClass.InternalFte, 175m),
            new RateCardCsvRow("UX Designer", "Boarding", Seniority.Associate, "Nearshore", ResourcingClass.Vendor, 42.25m)
        };
        var sw = new StringWriter();
        RateCardCsv.Write(sw, rows);

        var result = RateCardCsv.Parse(new StringReader(sw.ToString()));
        Assert.True(result.IsValid);
        Assert.Equal(rows, result.Rows);
    }
}
