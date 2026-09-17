namespace Notaker;

public sealed record VoiceModel(string Id, string Label, string Details, long MinimumBytes);

public static class VoiceModels
{
    public static readonly VoiceModel[] All =
    [
        new("base", "Base · rápido · 142 MiB", "Modelo ligero. Puede confundir nombres y palabras parecidas.", 140_000_000),
        new("small", "Small · equilibrado · 466 MiB", "Más capacidad que Base, con consumo moderado de memoria.", 480_000_000),
        new("large-v3-turbo-q8_0", "Large v3 Turbo · recomendado · 834 MiB", "Modelo avanzado cuantizado a 8 bits. Buen punto de partida para español e inglés; más lento que Small en algunos equipos.", 870_000_000),
        new("large-v3", "Large v3 · completo · 2,9 GiB", "Modelo completo sin cuantizar. Prioriza calidad; necesita más RAM y puede tardar bastante en CPU. No añade coste de API.", 3_000_000_000)
    ];
    public static bool IsSupported(string id) => All.Any(m => m.Id == id);
    public static VoiceModel Get(string id) => All.First(m => m.Id == id);
}
