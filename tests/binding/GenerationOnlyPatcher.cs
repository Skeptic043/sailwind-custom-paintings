using System;
using System.Reflection;
using HarmonyLib;
using HarmonyLib.Public.Patching;
using MonoMod.Utils;

public sealed class GenerationOnlyPatcher : MethodPatcher
{
    public GenerationOnlyPatcher(MethodBase original) : base(original) { }
    public override DynamicMethodDefinition PrepareOriginal() { return new DynamicMethodDefinition(Original); }
    public override DynamicMethodDefinition CopyOriginal() { return new DynamicMethodDefinition(Original); }

    // CLR cannot JIT Unity's engine internal calls. Keep Harmony's generated
    // replacement without installing a detour to the engine-dependent target.
    public override MethodBase DetourTo(MethodBase replacement) { return replacement; }

    public static void Register()
    {
        PatchManager.ClearAllPatcherResolvers();
        PatchManager.ResolvePatcher += (sender, args) =>
            args.MethodPatcher = new GenerationOnlyPatcher(args.Original);
    }

    public static void VerifyInvalidPrefixRejected(Harmony harmony)
    {
        var target = typeof(GenerationOnlyPatcher).GetMethod("ControlTarget");
        var prefix = typeof(GenerationOnlyPatcher).GetMethod("InvalidPrefix");
        var rejected = false;
        try { harmony.Patch(target, prefix: new HarmonyMethod(prefix)); }
        catch (HarmonyException exception)
        {
            if (!exception.ToString().Contains("Parameter \"missingArgument\" not found")) throw;
            rejected = true;
        }
        finally { harmony.Unpatch(target, prefix); }
        if (!rejected) throw new InvalidOperationException("Harmony accepted a nonexistent target argument.");
    }

    public static void ControlTarget(int value) { }
    public static void InvalidPrefix(int missingArgument) { }
}
