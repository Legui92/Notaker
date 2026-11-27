using Notaker;

/// <summary>Live context prompt: bounded tail of the previous fragments plus glossary, and the bench metrics.</summary>
static class LiveContextChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        string[] vocabulary = ["Nova-Hub = novahup", "Claude ~ cloud", ".NET", "UNA", "DeepSeek"];

        check(PersonalVocabulary.ContextTail(null) == "" && PersonalVocabulary.ContextTail("  \n ") == "", "No previous fragment gives no context");
        check(PersonalVocabulary.ContextTail("Revisa el\nbacklog   hoy") == "Revisa el backlog hoy", "Short context keeps every word and flattens line breaks");
        var words = string.Join(" ", Enumerable.Range(0, 120).Select(i => "palabra" + i));
        var tail = PersonalVocabulary.ContextTail(words);
        check(tail.Length <= PersonalVocabulary.MaxContextLength && words.EndsWith(tail) && words[words.Length - tail.Length - 1] == ' ', "Long context is bounded and starts at a word boundary: " + tail[..20]);
        var sentences = "uno dos tres cuatro cinco seis siete ocho nueve diez once doce. Abre el backlog y revisa el despliegue.";
        check(PersonalVocabulary.ContextTail(sentences, 60) == "Abre el backlog y revisa el despliegue.", "Context prefers to start at a sentence: " + PersonalVocabulary.ContextTail(sentences, 60));
        check(PersonalVocabulary.ContextTail(new string('a', 300)) == "", "A single word longer than the window is dropped, never cut in half");
        check(PersonalVocabulary.ContextTail(tail) == tail, "Trimming the context twice changes nothing");

        check(PersonalVocabulary.LivePrompt(vocabulary, "") == PersonalVocabulary.Prompt(vocabulary), "First fragment prompt is exactly the glossary prompt");
        check(PersonalVocabulary.LivePrompt([], "Hola equipo.") == "Hola equipo.", "Without vocabulary the prompt is the context alone");
        var live = PersonalVocabulary.LivePrompt(vocabulary, "abre el repo de novahup y pregunta a cloud");
        check(live.StartsWith("Glosario: ") && live.EndsWith("abre el repo de Nova-Hub y pregunta a cloud"), "Context follows the glossary with always-forms corrected and context-only words kept: " + live);
        var correct = PersonalVocabulary.LivePrompt(vocabulary, "Usa .NET 8 y UNA reunión.\nSale 20.26 el 2026");
        check(correct.EndsWith(" Usa .NET 8 y UNA reunión. Sale 20.26 el 2026"), "Already correct context (.NET, acronyms, numbers) is not rewritten: " + correct);
        var big = Enumerable.Range(0, 100).Select(i => "Terminología" + i).ToArray();
        var bounded = PersonalVocabulary.LivePrompt(big, words);
        check(bounded.Length <= PersonalVocabulary.MaxPromptLength && bounded.EndsWith(tail) && bounded.StartsWith("Glosario: "),
            $"Glossary shrinks so glossary plus context stay within the glossary-only bound ({bounded.Length})");

        var seen = new List<string>();
        await using (var session = new LiveTranscription((_, previous, _) => { lock (seen) seen.Add(previous); return Task.FromResult(seen.Count == 1 ? "Primer fragmento." : "Segundo."); }, CancellationToken.None))
        {
            for (var i = 0; i < 300; i++) session.Append(Enumerable.Repeat((byte)1, 3200).ToArray(), 3200, .1f);
            for (var i = 0; i < 7; i++) session.Append(new byte[3200], 3200, 0);
            session.Append([2, 3, 4, 5], 4, .1f);
            check(await session.CompleteAsync() == "Primer fragmento. Segundo." && seen.SequenceEqual(["", "Primer fragmento."]), "Each live fragment receives the text recognized before it");
        }

        check(VoiceEval.Occurrences("Nova-Hub-Alerts y Nova-Hub", "Nova-Hub") == 1 && VoiceEval.Occurrences("Backlog y backlog", "backlog") == 2
            && VoiceEval.Occurrences("Cloud y cloud", "Claude") == 0 && VoiceEval.Occurrences("Deepseek", "DeepSeek") == 0, "Bench term hits need the exact written spelling and whole words");
        check(VoiceEval.Repeats("abre el backlog ahora", "abre el backlog ahora") == 0 && VoiceEval.Repeats("abre el backlog ahora", "abre el backlog ahora abre el backlog ahora") > 0, "Bench counts repeated phrases");
        var reference = VoiceEval.ParseReference("Instrucciones.\n\nBloque 1\nHola Nova-Hub.\n\nBloque 2\nSegunda línea\ncontinúa.\n");
        check(reference.Count == 2 && reference[0] == (1, "Hola Nova-Hub.") && reference[1] == (2, "Segunda línea continúa."), "Bench reference parser reads numbered blocks only");
        var loud = Enumerable.Repeat((byte)40, 3200 * 70).ToArray(); var pause = new byte[3200 * 8]; var hum = Enumerable.Range(0, 3200 * 3).Select(i => (byte)(i % 2 == 0 ? 1 : 0)).ToArray();
        var pieces = VoiceEval.Segments([.. loud, .. pause, .. loud, .. pause, .. hum], 6);
        check(pieces.Count == 2 && pieces.Sum(p => p.Length) == loud.Length * 2 + pause.Length * 2 + hum.Length, "Bench fragments cut at pauses, keep every byte and join a quiet remainder to the last piece");
        var score = VoiceEval.Measure("Abre Nova-Hub y Claude.", "abre novahup y cloud.", ["Nova-Hub", "Claude"]);
        check(score.Words == 4 && score.Edits == 2 && score.Expected == 2 && score.Hits == 0 && score.Missed.SequenceEqual(["Nova-Hub", "Claude"]), "Bench WER ignores case and punctuation and lists missed terms");
    }
}
