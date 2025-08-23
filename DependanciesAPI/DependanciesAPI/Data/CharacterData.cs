using MareSynchronos.API.Data.Enum;
using MessagePack;
using System.Security.Cryptography;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MareSynchronos.API.Data;

[MessagePackObject(keyAsPropertyName: true)]
public class CharacterData
{
    public CharacterData()
    {
        DataHash = new(() =>
        {
            var json = JsonSerializer.Serialize(this);
#pragma warning disable SYSLIB0021 // Type or member is obsolete
            using SHA256CryptoServiceProvider cryptoProvider = new();
#pragma warning restore SYSLIB0021 // Type or member is obsolete
            return BitConverter.ToString(cryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "", StringComparison.Ordinal);
        });
    }

    public Dictionary<ObjectKind, string> CustomizePlusData { get; set; } = new();
    [JsonIgnore]
    public Lazy<string> DataHash { get; }

    public Dictionary<ObjectKind, List<FileReplacementData>> FileReplacements { get; set; } = new();
    public Dictionary<ObjectKind, string> GlamourerData { get; set; } = new();
    public string HeelsData { get; set; } = string.Empty;
    public string HonorificData { get; set; } = string.Empty;
    public string ManipulationData { get; set; } = string.Empty;
    public string MoodlesData { get; set; } = string.Empty;
    public string PetNamesData { get; set; } = string.Empty;
}