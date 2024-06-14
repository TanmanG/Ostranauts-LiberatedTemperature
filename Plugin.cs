using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using BepInEx;
using HarmonyLib;

namespace LiberatedTemperature
{
	public static class PluginInfo
	{
		public const string PLUGIN_GUID = "LiberatedTemperature";
		public const string PLUGIN_NAME = "Liberated Temperature";
		public const string PLUGIN_VERSION = "1.0.0";
	}

	[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
	public class Plugin : BaseUnityPlugin
	{
		public static Plugin Instance;
		
		public static void LogMessage(string message) => Instance.Logger.LogError($"{message}");
		public static void LogWarning(string message) => Instance.Logger.LogError($"{message}");
		public static void LogError(string message) => Instance.Logger.LogError($"{message}");
		
		private void Awake()
		{
			Instance = this;
			
			// Plugin startup logic
			LogMessage("Patching...");
			Harmony.CreateAndPatchAll(typeof(Plugin));
			LogMessage("Patched!");
			
			LogMessage($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
		}
		
		[HarmonyPatch(typeof(GUIOptions), "Init")]
		[HarmonyTranspiler]
		public static IEnumerable<CodeInstruction> PatchGUIOptions_AddFahrenheitEntry(IEnumerable<CodeInstruction> instructions)
		{
			CodeMatcher matcher;
			try
			{
				// Transpile Fahrenheit entry into GUIOptions.Init method.
				matcher = new CodeMatcher(instructions);
				matcher.MatchForward(false, // Find the branch destination label
				                     new CodeMatch(OpCodes.Ldarg_0),
				                     new CodeMatch(OpCodes.Ldfld),
				                     new CodeMatch(OpCodes.Ldstr, "Kelvin"),
				                     new CodeMatch(OpCodes.Ldc_I4_0),
				                     new CodeMatch(OpCodes.Stloc_S),
				                     new CodeMatch(OpCodes.Ldloca_S),
				                     new CodeMatch(OpCodes.Constrained),
				                     new CodeMatch(OpCodes.Callvirt),
				                     new CodeMatch(OpCodes.Callvirt));
				if (matcher.Remaining == 0)
					throw new Exception("Could not find the branch destination label.");
			}
			catch (Exception e)
			{
				LogError($"Exception during transpilation instruction matching: {e}");
				return instructions;
			}


			CodeInstruction[] addToDictionaryInstructions;
			try
			{
				// Insert the Fahrenheit entry.
				addToDictionaryInstructions = new CodeInstruction[5];

				addToDictionaryInstructions[0] = matcher.InstructionAt(0);
				addToDictionaryInstructions[1] = matcher.InstructionAt(1);
				addToDictionaryInstructions[2] = new CodeInstruction(OpCodes.Ldstr, "Fahrenheit");
				addToDictionaryInstructions[3] = new CodeInstruction(OpCodes.Ldstr, "F");
				addToDictionaryInstructions[4] = matcher.InstructionAt(8);
			}
			catch (Exception e)
			{
				LogError($"Exception while collecting transpilation instructions: {e}");
				return instructions;
			}

			try
			{
				// Insert the computed instructions.
				matcher.InsertAndAdvance(addToDictionaryInstructions);
			}
			catch (Exception e)
			{
				LogError($"Exception during transpilation instruction insertion: {e}");
				return instructions;
			}
			
			// Return the finished instructions.
			return matcher.InstructionEnumeration();
		}

		[HarmonyPatch(typeof(MathUtils), nameof(MathUtils.GetTemperatureString))]
		[HarmonyPrefix]
		public static bool PatchMathUtils_AddFahrenheitCase(double dfAmount, ref string __result)
		{
			Traverse fTempLast = Traverse.Create(typeof(MathUtils)).Field("fTempLast");
			Traverse strTemp = Traverse.Create(typeof(MathUtils)).Field("strTemp");
			if (dfAmount == fTempLast.GetValue<double>() && !string.IsNullOrEmpty(strTemp.GetValue<string>()))
			{
				__result = strTemp.GetValue<string>();
				return false;
			}
			
			Traverse sb = Traverse.Create(typeof(MathUtils)).Field("sb");
			sb.GetValue<StringBuilder>().Length = 0;
			double celsius = dfAmount - 273.15;
			
			switch (DataHandler.dictSettings["UserSettings"].TemperatureUnit())
			{
				case MathUtils.TemperatureUnit.K:
					sb.GetValue<StringBuilder>().Append(dfAmount.ToString("n2"));
					sb.GetValue<StringBuilder>().Append("K");
					break;
				case MathUtils.TemperatureUnit.C:
					sb.GetValue<StringBuilder>().Append(celsius.ToString("n2"));
					sb.GetValue<StringBuilder>().Append("C");
					break;
				
				// THIS CASE IS THE ONLY IMPORTANT PART OF CODE. REST CAN BE DISCARDED FOR SAKE OF MAINTAINABILITY.
				case (MathUtils.TemperatureUnit) 2: // MathUtils.TemperatureUnit.F
					double fahrenheit = celsius * 9.0 / 5.0 + 32.0;
					sb.GetValue<StringBuilder>().Append(fahrenheit.ToString("n2"));
					sb.GetValue<StringBuilder>().Append("F");
					break;
				// END OF IMPORTANT PART.
				
				default: // This is a small safeguard in case of unknown temperature unit that was not handled prior.
					LogWarning("Unknown temperature unit, defaulting to Kelvin.");
					sb.GetValue<StringBuilder>().Append(dfAmount.ToString("n2"));
					sb.GetValue<StringBuilder>().Append("K");
					break;
			}
			
			fTempLast.SetValue(dfAmount);
			strTemp.SetValue(sb.GetValue<StringBuilder>().ToString());
			__result = strTemp.GetValue<string>();
			return false;
		}
		
		[HarmonyPatch(typeof(JsonUserSettings), nameof(JsonUserSettings.TemperatureUnit))]
		[HarmonyPrefix]
		public static bool PatchJsonUserSettings_AddFahrenheitCondition(JsonUserSettings __instance, ref MathUtils.TemperatureUnit __result)
		{
			// Much cleaner method to handle temperature unit conversion.
			switch (__instance.strTemperatureUnit)
			{
				default:
				case "K":
					__result = MathUtils.TemperatureUnit.K;
					break;
				case "C":
					__result = MathUtils.TemperatureUnit.C;
					break;
				case "F":
					__result = (MathUtils.TemperatureUnit) 2;
					break;
			}
			
			return false;
		}
	}
}
