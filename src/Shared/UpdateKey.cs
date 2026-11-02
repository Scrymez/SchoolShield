namespace SchoolShield
{
    /// <summary>
    /// Public key used to verify update packages. The matching private key is kept offline by the author
    /// (never in this repository); an update that is not signed with it is rejected.
    /// </summary>
    internal static class UpdateKey
    {
        public const string PublicXml = "<RSAKeyValue><Modulus>mlQgPtsUBowvqMu3K1Hr7Jt7O+VrjHpN4X8S7G/npSllGqCrlC+P8v8a/f97z6RH9tAxHoA7OG1S3+NuCh9uTFeec2b0+YMnCycFTmAqqaMeap0oxgsipOpd5eAQZCTtFNJTLBECcKYVAyWUjmKldnwEHxyeXtpv6jBOP3mImbP0qe3NXuDTfz2+TJDye9J8q+QIxsZSebaorOk9cMIIkJnbQ+CJ/ZitYw0fjIXrDHuyEjiWY+EDiagg5yMAQ5qMFT25MIt9LfSTZVzQU3+PKDfa8KDxBqdHdnnzZUuZZ9/rfWpFsaMjOm2iGmDF7O1ozoOqLisIazad5gDxuzcw5d7eW74m6pad/Dd5L05th8vcXQlTUZL68ZNXSYlLyAfSlUJDEVXAHCkfOq7UzW+QUxZiVaWqxegbft9Vi8o5eJp1nUuuUQfJU2UvIdsFRlgW3rVonVw4wbvHTS5X+1Gew8B4FbTDZ/p7LTV8DgzjeGumQw5qTIfjz0cOZRu08ErD</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    }
}
