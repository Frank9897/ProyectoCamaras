using System.Security.Cryptography;
using System.Text;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Genera el header WS-Security UsernameToken con PasswordDigest que exige el estándar ONVIF
/// para autenticar contra el Media/PTZ/Imaging service (a diferencia del Device service,
/// que en muchos firmwares responde GetDeviceInformation sin login).
///
/// Digest = Base64( SHA1( Nonce (bytes) + Created (UTF8) + Password (UTF8) ) )
/// Esto es exactamente lo que pide la spec de WS-Security UsernameToken Profile 1.0.
/// </summary>
internal static class WsSecurityHeaderBuilder
{
    public static string Build(string username, string password)
    {
        var nonceBytes = RandomNumberGenerator.GetBytes(16);
        var nonceBase64 = Convert.ToBase64String(nonceBytes);
        var created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        var createdBytes = Encoding.UTF8.GetBytes(created);
        var passwordBytes = Encoding.UTF8.GetBytes(password);

        var toHash = new byte[nonceBytes.Length + createdBytes.Length + passwordBytes.Length];
        Buffer.BlockCopy(nonceBytes, 0, toHash, 0, nonceBytes.Length);
        Buffer.BlockCopy(createdBytes, 0, toHash, nonceBytes.Length, createdBytes.Length);
        Buffer.BlockCopy(passwordBytes, 0, toHash, nonceBytes.Length + createdBytes.Length, passwordBytes.Length);

#pragma warning disable SYSLIB0021 // SHA1 es el algoritmo que exige la spec ONVIF, no una elección nuestra.
        using var sha1 = SHA1.Create();
        var digestBytes = sha1.ComputeHash(toHash);
#pragma warning restore SYSLIB0021
        var digestBase64 = Convert.ToBase64String(digestBytes);

        return $"""
            <wsse:Security xmlns:wsse="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"
                            xmlns:wsu="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"
                            soap:mustUnderstand="1">
              <wsse:UsernameToken>
                <wsse:Username>{System.Security.SecurityElement.Escape(username)}</wsse:Username>
                <wsse:Password Type="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest">{digestBase64}</wsse:Password>
                <wsse:Nonce EncodingType="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary">{nonceBase64}</wsse:Nonce>
                <wsu:Created>{created}</wsu:Created>
              </wsse:UsernameToken>
            </wsse:Security>
            """;
    }
}
