using CluedIn.Core.Data;
using CluedIn.Core.Data.Vocabularies;

namespace CluedIn.ExternalSearch.Providers.RestApi.Vocabularies;
public class RestApiOrganizationVocabulary : SimpleVocabulary
{
    public RestApiOrganizationVocabulary()
    {
        VocabularyName = "RestApi Organization";
        KeyPrefix = "restApi.organization";
        KeySeparator = ".";
        Grouping = EntityType.Organization;

        this.ConfidenceScore = this.Add(new VocabularyKey("_cluedin_confidenceScore"));
    }

    public VocabularyKey ConfidenceScore { get; set; }
}