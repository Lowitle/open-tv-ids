using OpenTvIds.IdsBuilder;
using Xunit;

namespace OpenTvIds.IdsBuilder.Tests;

/// <summary>
/// Tests of <see cref="CrossIdTitle"/>: the comparison between the titles of the catalog and the titles of
/// the Wikipedia articles, which is the first piece of evidence of every match.
/// </summary>
/// <remarks>
/// The titles are the real ones of the catalog of 3Cat, which is where the traps come from: the Catalan
/// title of a Japanese series, the qualifier an article adds to an ambiguous name, and the article of a
/// title that names a country.
/// </remarks>
public class CrossIdTitleTests
{
    [Theory]
    [InlineData("Polònia", "polonia")]
    [InlineData("  Bola   de Drac Súper ", "bola de drac super")]
    [InlineData("Ranma 1/2", "ranma 1 2")]
    [InlineData("Merlí (sèrie de televisió)", "merli serie de televisio")]
    [InlineData("L'auca del senyor Esteve", "l auca del senyor esteve")]
    public void NormalizeIgnoresCaseAccentsAndPunctuation(string value, string expected)
        => Assert.Equal(expected, CrossIdTitle.Normalize(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void NormalizeReturnsEmptyWhenNothingIsLeft(string? value)
        => Assert.Equal(string.Empty, CrossIdTitle.Normalize(value));

    [Theory]
    [InlineData("Polònia (programa)", "Polònia")]
    [InlineData("Merlí (sèrie de televisió)", "Merlí")]
    [InlineData("Doraemon (2005 TV series)", "Doraemon")]
    [InlineData("Wonderblocks", "Wonderblocks")]
    [InlineData("(2005)", "(2005)")]
    public void BaseTitleRemovesTheQualifier(string value, string expected)
        => Assert.Equal(expected, CrossIdTitle.BaseTitle(value));

    [Theory]
    [InlineData("Polònia (programa)", "programa")]
    [InlineData("Bola de Drac (sèrie de televisió de 1986)", "sèrie de televisió de 1986")]
    [InlineData("Wonderblocks", null)]
    [InlineData(null, null)]
    public void QualifierReadsTheParenthesis(string? value, string? expected)
        => Assert.Equal(expected, CrossIdTitle.Qualifier(value));

    [Theory]
    [InlineData("Doraemon (2005)", 2005)]
    [InlineData("Doraemon (2005 TV series)", null)]
    [InlineData("Merlí (sèrie de televisió)", null)]
    [InlineData("Merlí", null)]
    public void QualifierYearReadsOnlyAYear(string? value, int? expected)
        => Assert.Equal(expected, CrossIdTitle.QualifierYear(value));

    [Theory]
    [InlineData("Polònia (programa)", true)]
    [InlineData("Merlí (sèrie de televisió)", true)]
    [InlineData("Doraemon (2005 TV series)", true)]
    [InlineData("Bola de Drac Z (pel·lícula)", true)]
    [InlineData("Doraemon (2005)", true)]
    [InlineData("Merlí", false)]
    [InlineData("Polònia", false)]
    [InlineData("Merlí (cavaller)", false)]
    [InlineData(null, false)]
    public void QualifierMentionsWorkTellsAWorkFromAProperName(string? title, bool expected)
        => Assert.Equal(expected, CrossIdTitle.QualifierMentionsWork(title));

    [Theory]
    [InlineData("Polònia", "Polònia", 3)]
    [InlineData("Polònia", "Polònia (programa)", 2)]
    [InlineData("Merlí", "Merlí (sèrie de televisió)", 2)]
    [InlineData("El detectiu Conan", "Detectiu Conan", 3)]
    [InlineData("Capitana Marleau", "La capitana Marleau", 3)]
    [InlineData("Crims", "Crims a Barcelona", 1)]
    [InlineData("Bola de Drac Z", "Bola de Drac", 1)]
    [InlineData("Crims al Bàltic", "Crims al Bàltic: Ombres nocturnes", 1)]
    [InlineData("Crims", "Bola de Drac", 0)]
    [InlineData("LIA", "Soledad Silveyra", 0)]
    public void CompareRanksTheRelationBetweenTwoTitles(string candidate, string article, int expected)
        => Assert.Equal((TitleMatch)expected, CrossIdTitle.Compare(candidate, article));

    [Fact]
    public void CompareRejectsTitlesThatAreOnlyOneLetter()
        => Assert.Equal(TitleMatch.None, CrossIdTitle.Compare("A", "A"));

    [Fact]
    public void CompareAcceptsAnArticleWithTheQualifierOfTheProgramme()
    {
        // The trap of the catalog: the article of the country comes first in the search of «Polònia», and the
        // programme is the article with the qualifier.
        Assert.Equal(TitleMatch.Exact, CrossIdTitle.Compare("Polònia", "Polònia"));
        Assert.Equal(TitleMatch.BaseTitle, CrossIdTitle.Compare("Polònia", "Polònia (programa)"));
    }
}
