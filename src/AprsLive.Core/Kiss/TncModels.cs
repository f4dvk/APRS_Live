namespace AprsLive.Core.Kiss;

/// <summary>
/// A kind of serial TNC, and the commands that put it into KISS mode and back
/// (see <see cref="TncScript"/>). They only fill in the commands, which stay
/// editable: firmware versions differ, and a model can be tuned to the radio.
/// </summary>
/// <param name="BaudRate">Serial speed the model's PC port uses by default, set when the model is picked; <c>null</c> to leave it.</param>
/// <param name="Notes">Shown under the choice: what to check on the equipment.</param>
public sealed record TncModel(string Id, string Label, string InitCommands, string ExitCommands, int? BaudRate, string Notes)
{
    public override string ToString() => Label;
}

public static class TncModels
{
    public const string NoneId = "none";
    public const string CustomId = "custom";

    public static TncModel None { get; } = new(NoneId, "TNC déjà en mode KISS (aucune commande)", "", "", null,
        "Rien n'est envoyé : le TNC doit déjà être en KISS (Direwolf, NinoTNC, Mobilinkd, TNC réglé une fois pour toutes…).");

    public static TncModel Tnc2 { get; } = new("tnc2", "TNC2 et compatibles (TAPR, MFJ, PacComm…)",
        """
        # Sortir d'un mode KISS resté actif
        {hex C0 FF C0}
        # Ctrl-C puis retour chariot : mode commande (cmd:)
        {hex 03 0D}
        KISS ON
        RESTART
        """,
        """
        # Retour au mode commande
        {hex C0 FF C0}
        """,
        null,
        "Firmware TNC2 d'origine et dérivés. Vitesse du port série : celle réglée sur le TNC.");

    public static TncModel Kantronics { get; } = new("kantronics", "Kantronics KPC-3, KPC-9612, KAM",
        """
        # Sortir d'un mode KISS resté actif
        {hex C0 FF C0}
        # Ctrl-C puis retour chariot : mode commande (cmd:)
        {hex 03 0D}
        INTFACE KISS
        RESET
        """,
        """
        # Retour au mode commande
        {hex C0 FF C0}
        """,
        null,
        "Vitesse du port série : celle réglée sur le TNC (ABAUD).");

    public static TncModel KenwoodD700 { get; } = new("tm-d700", "Kenwood TM-D700",
        """
        # Sortir d'un mode KISS resté actif
        {hex C0 FF C0 0D}
        # Commandes du poste
        TC 1
        {pause 1}
        # TNC en mode paquet (TNC 1 : APRS, TNC 0 : arrêt)
        TNC 2
        {pause 2}
        TC 0
        {pause 2}
        # Vitesse radio : HBAUD 1200 ou HBAUD 9600
        HBAUD 1200
        KISS ON
        RESTART
        """,
        """
        # Sortie du mode KISS
        {hex C0 FF C0}
        {pause 1}
        TC 1
        {pause 1}
        # TNC arrêté (TNC 1 : retour en mode APRS)
        TNC 0
        {pause 2}
        TC 0
        """,
        9600,
        "Port PC à 9600 bauds. La bande de données (A ou B) se choisit sur le poste (DATA BAND).");

    public static TncModel KenwoodD710 { get; } = new("tm-d710", "Kenwood TM-D710 / TM-D710G / TH-D72",
        """
        # Sortir d'un mode KISS resté actif
        {hex C0 FF C0 0D}
        # Commandes du poste
        TC 1
        {pause 1}
        # TNC en mode paquet sur la bande A (TN 2,1 : bande B)
        TN 2,0
        {pause 3}
        # Vitesse radio : HBAUD 1200 ou HBAUD 9600
        HBAUD 1200
        KISS ON
        RESTART
        """,
        """
        # Sortie du mode KISS
        {hex C0 FF C0}
        {pause 1}
        TC 1
        {pause 1}
        # TNC arrêté (TN 1,0 : retour en mode APRS, à vérifier sur votre poste)
        TN 0,0
        {pause 2}
        TC 0
        """,
        9600,
        "Port PC du panneau de commande, 9600 bauds. La bande est choisie par TN 2,0 (A) ou TN 2,1 (B).");

    public static TncModel Custom { get; } = new(CustomId, "Autre (commandes personnalisées)", "", "", null,
        "Écrivez les commandes de votre matériel : choisir ce modèle ne modifie pas celles déjà saisies.");

    public static IReadOnlyList<TncModel> All { get; } = [None, Tnc2, Kantronics, KenwoodD700, KenwoodD710, Custom];

    public static TncModel Find(string? id) => All.FirstOrDefault(m => m.Id == id) ?? None;
}
