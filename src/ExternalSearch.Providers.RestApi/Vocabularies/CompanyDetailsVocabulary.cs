//using CluedIn.Core.Data;
//using CluedIn.Core.Data.Vocabularies;

//namespace CluedIn.ExternalSearch.Providers.RestApi.Vocabularies
//{
//    public class CompanyDetailsVocabulary : SimpleVocabulary
//    {
//        public CompanyDetailsVocabulary()
//        {
//            VocabularyName = "GoogleMaps Organization";
//            KeyPrefix = "googleMaps.Organization";
//            KeySeparator = ".";
//            Grouping = EntityType.Organization;

//            AddGroup("CompanyDetails Organization Details", group =>
//            {
//                AddressComponents = group.Add(new VocabularyKey("AddressComponents", VocabularyKeyDataType.Json, VocabularyKeyVisibility.Hidden));
//                PostalCode = group.Add(new VocabularyKey("PostalCode", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                StreetNumber = group.Add(new VocabularyKey("StreetNumber", VocabularyKeyDataType.GeographyLocation, VocabularyKeyVisibility.Visible));
//                StreetName = group.Add(new VocabularyKey("StreetName", VocabularyKeyDataType.GeographyLocation, VocabularyKeyVisibility.Visible));
//                CityName = group.Add(new VocabularyKey("CityName", VocabularyKeyDataType.GeographyLocation, VocabularyKeyVisibility.Visible));
//                CountryCode = group.Add(new VocabularyKey("CountryCode", VocabularyKeyDataType.GeographyLocation, VocabularyKeyVisibility.Visible));
//                AdrAddress = group.Add(new VocabularyKey("AdrAddress", VocabularyKeyDataType.Html, VocabularyKeyVisibility.Visible));
//                FormattedAddress = group.Add(new VocabularyKey("FormattedAddress", VocabularyKeyDataType.GeographyLocation, VocabularyKeyVisibility.Visible));
//                FormattedPhoneNumber = group.Add(new VocabularyKey("FormattedPhoneNumber", VocabularyKeyDataType.PhoneNumber, VocabularyKeyVisibility.Visible));
//                //this.Geometry = group.Add(new VocabularyKey("Geometry", VocabularyKeyDataType.GeographyCoordinates, VocabularyKeyVisibility.Visible));
//                Icon = group.Add(new VocabularyKey("Icon", VocabularyKeyDataType.Uri, VocabularyKeyVisibility.Hidden));
//                Id = group.Add(new VocabularyKey("Id", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Hidden));
//                InternationalPhoneNumber = group.Add(new VocabularyKey("InternationalPhoneNumber", VocabularyKeyDataType.PhoneNumber, VocabularyKeyVisibility.Visible));
//                Name = group.Add(new VocabularyKey("Name", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                OpeningHours = group.Add(new VocabularyKey("OpeningHours", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                PlaceId = group.Add(new VocabularyKey("PlaceId", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Hidden));
//                PlusCode = group.Add(new VocabularyKey("PlusCode", VocabularyKeyDataType.Json, VocabularyKeyVisibility.Hidden));
//                Rating = group.Add(new VocabularyKey("Rating", VocabularyKeyDataType.Integer, VocabularyKeyVisibility.Hidden));
//                Reference = group.Add(new VocabularyKey("Reference", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Hidden));
//                Reviews = group.Add(new VocabularyKey("Reviews", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Hidden));
//                Scope = group.Add(new VocabularyKey("Scope", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Hidden));
//                Types = group.Add(new VocabularyKey("Types", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Hidden));
//                Url = group.Add(new VocabularyKey("Url", VocabularyKeyDataType.Uri, VocabularyKeyVisibility.Visible));
//                UserRatingsTotal = group.Add(new VocabularyKey("UserRatingsTotal", VocabularyKeyDataType.Integer, VocabularyKeyVisibility.Hidden));
//                UtcOffset = group.Add(new VocabularyKey("UtcOffset", VocabularyKeyDataType.Integer, VocabularyKeyVisibility.Visible));
//                Vicinity = group.Add(new VocabularyKey("Vicinity", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Hidden));
//                Website = group.Add(new VocabularyKey("Website", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                Latitude = group.Add(new VocabularyKey("Latitude", VocabularyKeyDataType.GeographyCoordinates, VocabularyKeyVisibility.Visible));
//                Longitude = group.Add(new VocabularyKey("Longitude", VocabularyKeyDataType.GeographyCoordinates, VocabularyKeyVisibility.Visible));

//                BusinessStatus = group.Add(new VocabularyKey("BusinessStatus", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                SubPremise = group.Add(new VocabularyKey("SubPremise", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Hidden));
//                AdministrativeAreaLevel1 = group.Add(new VocabularyKey("AdministrativeAreaLevel1", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                AdministrativeAreaLevel2 = group.Add(new VocabularyKey("AdministrativeAreaLevel2", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                Neighborhood = group.Add(new VocabularyKey("Neighborhood", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));

//                InternationalPhoneNumberCountry = group.Add(new VocabularyKey("InternationalPhoneNumber-Country", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                InternationalPhoneNumberE164 = group.Add(new VocabularyKey("InternationalPhoneNumber-E164", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                InternationalPhoneNumberInternational = group.Add(new VocabularyKey("InternationalPhoneNumber-International", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                InternationalPhoneNumberLocation = group.Add(new VocabularyKey("InternationalPhoneNumber-Location", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//                InternationalPhoneNumberRfc3966 = group.Add(new VocabularyKey("InternationalPhoneNumber-Rfc3966", VocabularyKeyDataType.Text, VocabularyKeyVisibility.Visible));
//            });

//        }

//        public VocabularyKey AddressComponents { get; set; }
//        public VocabularyKey PostalCode { get; set; }
//        public VocabularyKey StreetNumber { get; set; }
//        public VocabularyKey StreetName { get; set; }
//        public VocabularyKey CityName { get; set; }
//        public VocabularyKey CountryCode { get; set; }
//        public VocabularyKey AdrAddress { get; set; }
//        public VocabularyKey FormattedAddress { get; set; }
//        public VocabularyKey FormattedPhoneNumber { get; set; }
//        //public VocabularyKey Geometry { get; set; }
//        public VocabularyKey Icon { get; set; }
//        public VocabularyKey Id { get; set; }
//        public VocabularyKey InternationalPhoneNumber { get; set; }
//        public VocabularyKey Name { get; set; }
//        public VocabularyKey OpeningHours { get; set; }
//        public VocabularyKey PlaceId { get; set; }
//        public VocabularyKey PlusCode { get; set; }
//        public VocabularyKey Rating { get; set; }
//        public VocabularyKey Reference { get; set; }
//        public VocabularyKey Reviews { get; set; }
//        public VocabularyKey Scope { get; set; }
//        public VocabularyKey Types { get; set; }
//        public VocabularyKey Url { get; set; }
//        public VocabularyKey UserRatingsTotal { get; set; }
//        public VocabularyKey UtcOffset { get; set; }
//        public VocabularyKey Vicinity { get; set; }
//        public VocabularyKey Website { get; set; }
//        public VocabularyKey Latitude { get; set; }
//        public VocabularyKey Longitude { get; set; }
//        public VocabularyKey SubPremise { get; internal set; }
//        public VocabularyKey AdministrativeAreaLevel1 { get; internal set; }
//        public VocabularyKey AdministrativeAreaLevel2 { get; internal set; }
//        public VocabularyKey Neighborhood { get; internal set; }
//        public VocabularyKey BusinessStatus { get; internal set; }
//        public VocabularyKey InternationalPhoneNumberCountry { get; internal set; }
//        public VocabularyKey InternationalPhoneNumberE164 { get; internal set; }
//        public VocabularyKey InternationalPhoneNumberInternational { get; internal set; }
//        public VocabularyKey InternationalPhoneNumberLocation { get; internal set; }
//        public VocabularyKey InternationalPhoneNumberRfc3966 { get; internal set; }
//    }

//}