namespace OpenTvIds.IdsBuilder;

/// <summary>
/// An article of a Wikipedia that publishes a Wikidata item, as returned by the search endpoint.
/// </summary>
/// <param name="Language">Language of the Wikipedia («ca», «es», «en»).</param>
/// <param name="Title">Title of the article.</param>
/// <param name="Qid">Wikidata item of the article (<c>Q3490290</c>).</param>
/// <param name="Index">Position of the article in the answer of the search, 0 being the best one.</param>
internal sealed record ArticleHit(string Language, string Title, string Qid, int Index);

/// <summary>
/// The article the matcher has chosen for a programme, with the evidence the choice is based on.
/// </summary>
/// <param name="Hit">The article.</param>
/// <param name="Match">How well its title matches the title that was searched.</param>
/// <param name="SearchedTitle">Title that was looked up, which may be the original title.</param>
internal sealed record ArticleMatch(ArticleHit Hit, TitleMatch Match, string SearchedTitle);
