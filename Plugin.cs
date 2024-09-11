using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace LiberatedTemperature
{
	public static class PluginInfo
	{
		public const string PLUGIN_GUID = "LiberatedTemperature";
		public const string PLUGIN_NAME = "Liberated Temperature";
		public const string PLUGIN_VERSION = "1.2.0";
	}

	[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
	public class Plugin : BaseUnityPlugin
	{
		public static Plugin Instance;
		
		public static ConfigEntry<bool> SecondaryTemperatureEnabled;
		public static ConfigEntry<string> SecondaryTemperatureUnit;
		
		public static void LogMessage(string message) => Instance.Logger.LogError($"{message}");
		public static void LogWarning(string message) => Instance.Logger.LogError($"{message}");
		public static void LogError(string message) => Instance.Logger.LogError($"{message}");
		
		private void Awake()
		{
			Instance = this;
			
			// Load config variables.
			LogMessage("Reading Config...");
			SecondaryTemperatureEnabled = Config.Bind(section: "Second Unit",
			                                          key: "EnableSecondUnit",
			                                          defaultValue: false,
			                                          description: "Whether a second unit should be used or not.");
			SecondaryTemperatureUnit = Config.Bind(section: "Second Unit",
			                                       key: "SelectedUnit",
			                                       defaultValue: "K",
			                                       description: "The unit of the second temperature. Valid options: K, C, F, R.");
			LogMessage("Config read!");
			
			// Perform patching.
			LogMessage("Patching...");
			Harmony.CreateAndPatchAll(typeof(Plugin));
			LogMessage("Patched!");
			
			LogMessage($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
		}
		
		[HarmonyPatch(typeof(GUIOptions), "Init")]
		[HarmonyTranspiler]
		public static IEnumerable<CodeInstruction> PatchGUIOptions_AddEntry(IEnumerable<CodeInstruction> instructions)
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
			
			
			// Create the Fahrenheit instructions.
			CodeInstruction[] addFahrenheitToDictionaryInstructions;
			try
			{
				// Insert the Fahrenheit entry.
				addFahrenheitToDictionaryInstructions = new CodeInstruction[5];

				addFahrenheitToDictionaryInstructions[0] = matcher.InstructionAt(0);
				addFahrenheitToDictionaryInstructions[1] = matcher.InstructionAt(1);
				addFahrenheitToDictionaryInstructions[2] = new CodeInstruction(OpCodes.Ldstr, "Fahrenheit");
				addFahrenheitToDictionaryInstructions[3] = new CodeInstruction(OpCodes.Ldstr, "F");
				addFahrenheitToDictionaryInstructions[4] = matcher.InstructionAt(8);
			}
			catch (Exception e)
			{
				LogError($"Exception while collecting Fahrenheit transpilation instructions: {e}");
				return instructions;
			}

			// Create the Rankine instructions.
			CodeInstruction[] addRankineToDictionaryInstructions;
			try
			{
				addRankineToDictionaryInstructions = new CodeInstruction[5];
				
				for (int i = 0; i < addFahrenheitToDictionaryInstructions.Length; i++)
				{
					addRankineToDictionaryInstructions[i] = new CodeInstruction(addFahrenheitToDictionaryInstructions[i]);
				}

				addRankineToDictionaryInstructions[2].operand = "Rankine";
				addRankineToDictionaryInstructions[3].operand = "R";
			}
			catch (Exception e)
			{
				LogError($"Exception while collecting Rankine transpilation instructions: {e}");
				return instructions;
			}
			
			// Insert the Fahrenheit entry.
			try
			{
				// Insert the computed instructions.
				matcher.InsertAndAdvance(addFahrenheitToDictionaryInstructions);
			}
			catch (Exception e)
			{
				LogError($"Exception during Fahrenheit transpilation instruction insertion: {e}");
				return instructions;
			}
			
			// Insert the Rankine entry.
			try
			{
				// Insert the computed instructions.
				matcher.InsertAndAdvance(addRankineToDictionaryInstructions);
			}
			catch (Exception e)
			{
				LogError($"Exception during Rankine transpilation instruction insertion: {e}");
				return instructions;
			}
			
			
			// Return the finished instructions.
			return matcher.InstructionEnumeration();
		}

		[HarmonyPatch(typeof(MathUtils), nameof(MathUtils.GetTemperatureString))]
		[HarmonyPrefix]
		public static bool PatchMathUtils_AddCases(double dfAmount, ref string __result)
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
				case (MathUtils.TemperatureUnit) 3:
					double rankine = dfAmount * 1.8f;
					sb.GetValue<StringBuilder>().Append(rankine.ToString("n2"));
					sb.GetValue<StringBuilder>().Append("R");
					break;
				// END OF IMPORTANT PART.
				
				default: // This is a small safeguard in case of unknown temperature unit that was not handled prior.
					LogWarning("Unknown temperature unit, defaulting to Kelvin.");
					sb.GetValue<StringBuilder>().Append(dfAmount.ToString("n2"));
					sb.GetValue<StringBuilder>().Append("K");
					break;
			}

			if (SecondaryTemperatureEnabled.Value)
			{
				// Insert the divider to the temperature string.
				sb.GetValue<StringBuilder>().Append(" | ");
				
				// Then, add the secondary temperature unit.
				switch (SecondaryTemperatureUnit.Value)
				{
					case "K":
						sb.GetValue<StringBuilder>().Append(dfAmount.ToString("n2"));
						sb.GetValue<StringBuilder>().Append("K");
						break;
					case "C":
						sb.GetValue<StringBuilder>().Append(celsius.ToString("n2"));
						sb.GetValue<StringBuilder>().Append("C");
						break;
					case "R":
						double rankine = dfAmount * 1.8f;
						sb.GetValue<StringBuilder>().Append(rankine.ToString("n2"));
						sb.GetValue<StringBuilder>().Append("R");
						break;
					case "F":
						double fahrenheit = celsius * 9.0f / 5.0f + 32.0f;
						sb.GetValue<StringBuilder>().Append(fahrenheit.ToString("n2"));
						sb.GetValue<StringBuilder>().Append("F");
						break;
				}
			}	
			
			fTempLast.SetValue(dfAmount);
			strTemp.SetValue(sb.GetValue<StringBuilder>().ToString());
			__result = strTemp.GetValue<string>();
			return false;
		}
		
		[HarmonyPatch(typeof(JsonUserSettings), nameof(JsonUserSettings.TemperatureUnit))]
		[HarmonyPrefix]
		public static bool PatchJsonUserSettings_AddConditions(JsonUserSettings __instance, ref MathUtils.TemperatureUnit __result)
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
				case "R":
					__result = (MathUtils.TemperatureUnit) 3;
					break;
			}
			
			return false;
		}
	}
}
