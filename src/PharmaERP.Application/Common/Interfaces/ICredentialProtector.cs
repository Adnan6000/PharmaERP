namespace PharmaERP.Application.Common.Interfaces;

/// <summary>
/// Service contract for encrypting and decrypting sensitive credentials (e.g. via Windows DPAPI).
/// </summary>
public interface ICredentialProtector
{
    string? Protect(string? clearText);
    string? Unprotect(string? cipherText);
}

