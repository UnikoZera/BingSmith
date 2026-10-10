using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Nodes.Vfx;
namespace BingSmith;

[HarmonyPatch]
internal static class SmithAnimationSoundPatch
{
    private static readonly MethodInfo AudioPlay = AccessTools.Method(
        typeof(NDebugAudioManager), nameof(NDebugAudioManager.Play),
        new[] { typeof(string), typeof(float), typeof(PitchVariance) })!;
    private static readonly MethodInfo ReplacementPlay = AccessTools.Method(
        typeof(BingSmithAudio), nameof(BingSmithAudio.PlayFromSmithVfx))!;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var smithType = typeof(NCardSmithVfx);
        var candidates = new HashSet<MethodBase>();

        foreach (var animation in smithType.GetMethods(flags).Where(method => method.Name == "PlayAnimation"))
        {
            var stateMachine = animation.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
            var moveNext = stateMachine?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (moveNext != null)
                candidates.Add(moveNext);
        }

        foreach (var generatedMethod in smithType.GetMethods(flags)
                     .Where(method => method.Name.Contains("PlayAnimation", StringComparison.Ordinal)))
            candidates.Add(generatedMethod);

        foreach (var candidate in candidates)
        {
            if (ContainsSmithSoundCall(candidate))
                yield return candidate;
        }
    }

    private static bool ContainsSmithSoundCall(MethodBase method)
    {
        try
        {
            return PatchProcessor.GetOriginalInstructions(method).Any(instruction => instruction.Calls(AudioPlay));
        }
        catch (Exception exception)
        {
            GD.PushWarning($"BingSmith could not inspect {method} for the Smith sound call: {exception.Message}");
            return false;
        }
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var declaringType = __originalMethod.DeclaringType;
        var vfxField = declaringType == typeof(NCardSmithVfx)
            ? null
            : declaringType?.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(field => field.FieldType == typeof(NCardSmithVfx));
        if (declaringType != typeof(NCardSmithVfx) && vfxField == null)
        {
            GD.PushError($"BingSmith could not find the owning VFX instance in {declaringType}.");
            return instructions;
        }

        var output = new List<CodeInstruction>();
        var replacementCount = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(AudioPlay))
            {
                var loadVfx = new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0);
                loadVfx.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                output.Add(loadVfx);
                if (vfxField != null)
                    output.Add(new CodeInstruction(System.Reflection.Emit.OpCodes.Ldfld, vfxField));
                instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                instruction.operand = ReplacementPlay;
                replacementCount++;
            }

            output.Add(instruction);
        }

        if (replacementCount != 1)
            GD.PushError($"BingSmith expected one Smith sound call in {__originalMethod}, found {replacementCount}.");
        return output;
    }
}
