using System.Security.Cryptography;
using System.Text;
using PharmaERP.Application.Common.Interfaces;

namespace PharmaERP.Infrastructure.Security;

/// <summary>
/// Protects sensitive credentials using Windows Data Protection API (DPAPI) scoped to the current user.
/// Passwords protected this way can only be decrypted by the same Windows user account on the same machine.
/// </summary>
public class DpapiCredentialProtector : ICredentialProtector
{
    private static readonly byte[] Entropy = "PharmaERP_Workstation_Entropy_v1"u8.ToArray();

    public string? Protect(string? clearText)
    {
        if (string.IsNullOrEmpty(clearText))
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            // Non-Windows fallback for cross-platform test environments
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(clearText));
        }

        byte[] clearBytes = Encoding.UTF8.GetBytes(clearText);
        byte[] cipherBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipherBytes);
    }

    public string? Unprotect(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return null;
        }

        try
        {
            byte[] cipherBytes = Convert.FromBase64String(cipherText);

            if (!OperatingSystem.IsWindows())
            {
                // Non-Windows fallback
                return Encoding.UTF8.GetString(cipherBytes);
            }

            byte[] clearBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clearBytes);
        }
        catch
        {
            return null;
        }
    }
}

