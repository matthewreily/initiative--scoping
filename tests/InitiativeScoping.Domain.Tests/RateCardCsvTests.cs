using InitiativeScoping.Application.RateCards;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Tests;

public class RateCardCsvTests
{
    private const string Header = "ResourceType,Seniority,Location,ResourcingClass,HourlyRate\n";

    [Fact]
    public void Parses_valid_rows_with_class_aliases_and_trimming()
    {
        var result = RateCardCsv.Parse(new StringReader(Header +
            " Software Engineer , senior , Onshore , Internal , 120.50\n" +
            "QA Analyst,Mid,Offshore,Contractor,$55\n"));

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(new RateCardCsvRow("Software Engineer", "senior", "Onshore", ResourcingClass.InternalFte, 120.50m), result.Rows[0]);
        Assert.Equal(ResourcingClass.Vendor, result.Rows[1].ResourcingClass);
        Assert.Equal(55m, result.Rows[1].HourlyRate);
    }

    [Fact]
    public void Legacy_business_unit_column_is_ignored()
    {
        var result = RateCardCsv.Parse(new StringReader("ResourceType,BusinessUnit,Seniority,Location,ResourcingClass,HourlyRate\n" +
            "SE,Boarding,Senior,Onshore,Internal,100\n" +
            "SE,Lending,Senior,Onshore,Internal,110\n"));

        Assert.False(result.IsValid);
        Assert.Contains("Duplicate", Assert.Single(result.Errors).Message);
        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public void Optional_discipline_column_is_carried_and_length_checked()
    {
        var result = RateCardCsv.Parse(new StringReader("ResourceType,Seniority,Location,ResourcingClass,HourlyRate,Vendor,Discipline\n" +
            "Agile Practitioner,Senior,Onshore,Internal,100,,\n" +
            "AI Engineer,Senior,Onshore,Internal,150,, Data Science \n" +
            $"Business Analyst,Senior,Onshore,Internal,90,,{new string('x', 101)}\n"));

        Assert.Null(result.Rows[0].Discipline);
        Assert.Equal("Data Science", result.Rows[1].Discipline);
        Assert.Contains("Discipline must be at most", Assert.Single(result.Errors).Message);

        var sw = new StringWriter();
        RateCardCsv.Write(sw, result.Rows);
        Assert.StartsWith(string.Join(',', RateCardCsv.Headers), sw.ToString());
        Assert.Contains("AI Engineer,Senior,Onshore,InternalFte,150.00,,Data Science", sw.ToString());
    }

    [Fact]
    public void Reports_missing_columns()
    {
        var result = RateCardCsv.Parse(new StringReader("ResourceType,Location\nA,B\n"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("Missing column"));
    }

    [Fact]
    public void Reports_invalid_values_with_line_numbers()
    {
        var result = RateCardCsv.Parse(new StringReader(Header +
            "SE,,Onshore,Internal,100\n" +
            "SE,Senior,Onshore,Freelance,100\n" +
            "SE,Senior,Onshore,Internal,-5\n" +
            ",Senior,Onshore,Internal,100\n"));

        Assert.False(result.IsValid);
        Assert.Empty(result.Rows);
        Assert.Equal([2, 3, 4, 5], result.Errors.Select(e => e.Line).ToArray());
    }

    [Fact]
    public void Rejects_seniority_names_over_the_catalog_limit()
    {
        var result = RateCardCsv.Parse(new StringReader(Header + $"SE,{new string('x', 101)},Onshore,Internal,100\n"));

        Assert.False(result.IsValid);
        Assert.Contains("at most 100 characters", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void Accepts_any_seniority_name()
    {
        var result = RateCardCsv.Parse(new StringReader(Header + "SE,Level 1 (0-2 Years),Onshore,Internal,100\n"));

        Assert.True(result.IsValid);
        Assert.Equal("Level 1 (0-2 Years)", Assert.Single(result.Rows).Seniority);
    }

    [Fact]
    public void Detects_duplicate_dimension_tuples_case_insensitively()
    {
        var result = RateCardCsv.Parse(new StringReader(Header +
            "SE,Senior,Onshore,Internal,100\n" +
            "se,SENIOR,onshore,Fte,110\n"));

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Duplicate", result.Errors[0].Message);
    }

    [Fact]
    public void Write_then_parse_round_trips()
    {
        var rows = new[]
        {
            new RateCardCsvRow("Software Engineer", "Staff", "Onshore", ResourcingClass.InternalFte, 175m),
            new RateCardCsvRow("UX Designer", "Level 1 (0-2 Years)", "Nearshore", ResourcingClass.Vendor, 42.25m)
        };
        var sw = new StringWriter();
        RateCardCsv.Write(sw, rows);

        var result = RateCardCsv.Parse(new StringReader(sw.ToString()));
        Assert.True(result.IsValid);
        Assert.Equal(rows, result.Rows);
    }
}
