using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.App.Security;

/// <summary>
/// Implementación de ICredentialStore basada en Windows Credential Manager.
/// La contraseña se entrega a la API nativa y no se persiste en SQLite.
/// </summary>
public sealed class WindowsCredentialStore : ICredentialStore
{
    // CredentialTypeGeneric representa credenciales genéricas de Windows apropiadas
    // para almacenar usuario y contraseña de un dispositivo de red.
    private const uint CredentialTypeGeneric = 1;

    // CredentialPersistLocalMachine mantiene la credencial disponible de forma persistente
    // para el usuario actual del equipo entre ejecuciones de la aplicación.
    private const uint CredentialPersistLocalMachine = 2;

    public Task<Guid> SaveAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // credentialRef es la referencia opaca que SQLite guardará en lugar del secreto.
        var credentialRef = Guid.NewGuid();

        // targetName identifica de forma única la entrada dentro de Windows Credential Manager.
        var targetName = BuildTargetName(credentialRef);

        // credentialBlob contiene temporalmente la contraseña convertida a UTF-16 para la API nativa.
        var credentialBlob = Encoding.Unicode.GetBytes(password + "\0");

        // targetNamePtr y userNamePtr son bloques de memoria nativa que Windows espera como nint.
        var targetNamePtr = Marshal.StringToHGlobalUni(targetName);
        var userNamePtr = Marshal.StringToHGlobalUni(username);

        // credentialBlobPtr contiene temporalmente la contraseña en memoria nativa.
        var credentialBlobPtr = Marshal.AllocHGlobal(credentialBlob.Length);

        try
        {
            // Copiamos la contraseña desde memoria administrada al bloque nativo requerido por Windows.
            Marshal.Copy(credentialBlob, 0, credentialBlobPtr, credentialBlob.Length);

            // nativeCredential describe completamente la entrada que vamos a registrar en Windows.
            var nativeCredential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = targetNamePtr,
                UserName = userNamePtr,
                CredentialBlobSize = (uint)credentialBlob.Length,
                Persist = CredentialPersistLocalMachine,
                CredentialBlob = credentialBlobPtr
            };

            // CredWrite almacena la credencial en el almacén seguro de Windows.
            if (!CredWrite(ref nativeCredential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "No se pudo guardar la credencial en Windows Credential Manager.");
            }
        }
        finally
        {
            // Liberamos todos los bloques nativos creados para esta operación, incluso si CredWrite falla.
            Marshal.FreeHGlobal(credentialBlobPtr);
            Marshal.FreeHGlobal(targetNamePtr);
            Marshal.FreeHGlobal(userNamePtr);

            // Limpiamos la referencia administrada inmediatamente después de liberar el bloque nativo.
            Array.Clear(credentialBlob, 0, credentialBlob.Length);
        }

        return Task.FromResult(credentialRef);
    }

    public Task<StoredCredential?> GetAsync(
        Guid credentialRef,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // targetName reconstruye exactamente el nombre usado al guardar la credencial.
        var targetName = BuildTargetName(credentialRef);

        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();

            // ERROR_NOT_FOUND significa que la referencia existe en SQLite pero la credencial
            // ya no existe en Windows. En ese caso devolvemos null para permitir que la UI pida otra.
            if (error == 1168)
                return Task.FromResult<StoredCredential?>(null);

            throw new Win32Exception(error, "No se pudo leer la credencial desde Windows Credential Manager.");
        }

        try
        {
            // nativeCredential es la estructura que Windows devuelve apuntando al secreto protegido.
            var nativeCredential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            var username = PtrToString(nativeCredential.UserName);
            var password = ReadCredentialBlob(
                nativeCredential.CredentialBlob,
                nativeCredential.CredentialBlobSize);

            return Task.FromResult<StoredCredential?>(new StoredCredential(username, password));
        }
        finally
        {
            // CredFree libera toda la estructura asignada por CredRead, incluido el bloque del secreto.
            CredFree(credentialPtr);
        }
    }

    public Task DeleteAsync(
        Guid credentialRef,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // targetName identifica la credencial concreta que queremos eliminar.
        var targetName = BuildTargetName(credentialRef);

        if (!CredDelete(targetName, CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();

            // Si ya no existe, la operación es idempotente y no necesitamos tratarla como error.
            if (error != 1168)
            {
                throw new Win32Exception(error, "No se pudo eliminar la credencial de Windows Credential Manager.");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Genera el nombre interno de Windows para nuestra credencial.
    /// El GUID es suficiente para evitar colisiones y no expone IP ni contraseña.
    /// </summary>
    private static string BuildTargetName(Guid credentialRef) =>
        $"CameraInspector:{credentialRef:D}";

    private static string PtrToString(IntPtr pointer) =>
        pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(pointer) ?? string.Empty;

    private static string ReadCredentialBlob(IntPtr blob, uint size)
    {
        if (blob == IntPtr.Zero || size == 0)
            return string.Empty;

        // byteCount evita leer más memoria de la que Windows indicó explícitamente.
        var byteCount = checked((int)size);
        var bytes = new byte[byteCount];

        try
        {
            Marshal.Copy(blob, bytes, 0, byteCount);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally
        {
            // Eliminamos el contenido intermedio antes de devolver el string a la aplicación.
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(
        ref NativeCredential credential,
        uint flags);

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(
        string target,
        uint type,
        uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
