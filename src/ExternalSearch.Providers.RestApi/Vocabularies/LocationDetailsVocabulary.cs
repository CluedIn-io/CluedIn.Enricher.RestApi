//using CluedIn.Core.Data;
//using CluedIn.Core.Data.Vocabularies;

//namespace CluedIn.ExternalSearch.Providers.RestApi.Vocabularies
//{
//    public class LocationDetailsVocabulary : SimpleVocabulary
//    {
//        public LocationDetailsVocabulary()
//        {
//            VocabularyName = "GoogleMaps Location";
//            KeyPrefix = "googleMaps.Location";
//            KeySeparator = ".";
//            Grouping = EntityType.Location;

//            AddGroup("Location Details", group =>
//            {

//                FormattedAddress = group.Add(new VocabularyKey("formattedAddress", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                Geometry = group.Add(new VocabularyKey("Geometry", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Hidden));
//                Latitude = group.Add(new VocabularyKey("Latitude", VocabularyKeyDataType.GeographyCoordinates, VocabularyKeyVisibility.Hidden));
//                Longitude = group.Add(new VocabularyKey("Longitude", VocabularyKeyDataType.GeographyCoordinates, VocabularyKeyVisibility.Hidden));
//                Name = group.Add(new VocabularyKey("Name", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                NameStreet = group.Add(new VocabularyKey("NameStreet", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                NumberStreet = group.Add(new VocabularyKey("NumberStreet", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                NameCity = group.Add(new VocabularyKey("NameCity", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                CodeCountry = group.Add(new VocabularyKey("CodeCountry", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                CodePostal = group.Add(new VocabularyKey("CodePostal", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                AdministrativeArea = group.Add(new VocabularyKey("AdministrativeArea", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                ComponentsAddress = group.Add(new VocabularyKey("componentsAddress", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//            });


//        }


//        public VocabularyKey FormattedAddress { get; set; }
//        public VocabularyKey Geometry { get; set; }
//        public VocabularyKey Name { get; set; }
//        public VocabularyKey NameStreet { get; set; }
//        public VocabularyKey NumberStreet { get; set; }
//        public VocabularyKey NameCity { get; set; }
//        public VocabularyKey CodeCountry { get; set; }
//        public VocabularyKey CodePostal { get; set; }
//        public VocabularyKey AdministrativeArea { get; set; }
//        public VocabularyKey ComponentsAddress { get; set; }
//        public VocabularyKey Latitude { get; set; }
//        public VocabularyKey Longitude { get; set; }

//    }

//}