using ScotTrip.Models;

namespace ScotTrip.Components;

/// <summary>Carattere visivo di ogni tipo di tappa: icona, etichetta e colore d'accento.</summary>
public static class KindInfo
{
    public static string Icon(this StopKind kind) => kind switch
    {
        StopKind.Castle => "🏰",
        StopKind.Nature => "🌿",
        StopKind.Beach => "🐚",
        StopKind.Distillery => "🥃",
        StopKind.Church => "⛪",
        StopKind.Viewpoint => "🔭",
        StopKind.Village => "🏘",
        StopKind.City => "🎡",
        _ => "🗿"
    };

    public static string Label(this StopKind kind) => kind switch
    {
        StopKind.Castle => "Castello",
        StopKind.Nature => "Natura",
        StopKind.Village => "Borgo",
        StopKind.Beach => "Spiaggia",
        StopKind.Distillery => "Distilleria",
        StopKind.Church => "Cattedrale",
        StopKind.Viewpoint => "Panorama",
        StopKind.City => "Città",
        _ => "Da vedere"
    };

    /// <summary>Colore d'accento della pagina tappa (bordi, dettagli).</summary>
    public static string Accent(this StopKind kind) => kind switch
    {
        StopKind.Castle => "#22384E",
        StopKind.Nature => "#2E5B4E",
        StopKind.Beach => "#3E7C8A",
        StopKind.Distillery => "#9A6B2F",
        StopKind.Church => "#6A5490",
        StopKind.Viewpoint => "#B08A28",
        StopKind.Village => "#7A5B43",
        StopKind.City => "#4A5560",
        _ => "#7A5F9E"
    };
}
