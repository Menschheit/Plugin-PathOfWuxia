﻿using BepInEx.Configuration;
using HarmonyLib;
using Heluo;
using Heluo.Data;
using Heluo.Features;
using Heluo.Flow;
using Heluo.UI;
using Heluo.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PathOfWuxia
{
    [System.ComponentModel.DisplayName("角色设定")]
    [System.ComponentModel.Description("修改建模、头像、姓名")]
    class HookCharacterExterior : IHook
    {
        static ConfigEntry<string> playerExteriorId;
        static ConfigEntry<string> playerPortraitOverride;
        static ConfigEntry<string> playerSurNameOverride;
        static ConfigEntry<string> playerNameOverride;
        static EventHandler ReplacePlayerExteriorDataEventHander;
        public void OnRegister(PluginBinarizer plugin)
        {
            playerExteriorId = plugin.Config.Bind("角色设定", "主角建模", string.Empty, "设定主角建模数据源");
            playerPortraitOverride = plugin.Config.Bind("角色设定", "主角头像", string.Empty, "若已设置建模，则可为空，使用建模的头像，否则用此头像代替");
            playerSurNameOverride = plugin.Config.Bind("角色设定", "主角姓", "亦", "可修改主角的姓");
            playerNameOverride = plugin.Config.Bind("角色设定", "主角名", "天凛", "可修改主角的名");

            ReplacePlayerExteriorDataEventHander += new EventHandler((o, e) =>
            {
                ReplacePlayerExteriorData();
            });

            playerExteriorId.SettingChanged += ReplacePlayerExteriorDataEventHander;
            playerPortraitOverride.SettingChanged += ReplacePlayerExteriorDataEventHander;
            playerSurNameOverride.SettingChanged += ReplacePlayerExteriorDataEventHander;
            playerNameOverride.SettingChanged += ReplacePlayerExteriorDataEventHander;
        }

        // 4 头像模型名称性别替换
        public static void ReplacePlayerExteriorData()
        {
            Console.WriteLine("ReplacePlayerExteriorData start");
            string[] characters = new string[] { GameConfig.Player, "in0196", "in0197", "in0101", "in0115" };
            for (int i = 0; i < characters.Length; i++)
            {
                CharacterExteriorData playerExteriorData = Game.GameData.Exterior[characters[i]];
                if (playerExteriorData != null && !playerExteriorId.Value.Trim().IsNullOrEmpty())
                {
                    foreach (KeyValuePair<string, CharacterExterior> kv in Game.Data.Get<CharacterExterior>())
                    {
                        if (kv.Value.Model == playerExteriorId.Value.Trim())
                        {
                            playerExteriorData.Id = kv.Value.Id;
                            playerExteriorData.Model = kv.Value.Model;
                            playerExteriorData.Gender = kv.Value.Gender;
                            playerExteriorData.Size = kv.Value.Size;
                            playerExteriorData.Protrait = kv.Value.Protrait;
                            Console.WriteLine("id:"+kv.Value.Id+ ",Model:" + kv.Value.Model+ ",Gender:" + kv.Value.Gender + ",Size:" + kv.Value.Size + ",Protrait:" + kv.Value.Protrait + ",");
                            break;
                        }
                    }
                }
                if (!playerPortraitOverride.Value.Trim().IsNullOrEmpty())
                {
                    foreach (KeyValuePair<string, CharacterExterior> kv in Game.Data.Get<CharacterExterior>())
                    {
                        if (kv.Value.Protrait == playerPortraitOverride.Value.Trim())
                        {
                            playerExteriorData.Protrait = kv.Value.Protrait;
                            Console.WriteLine("Protrait:" + kv.Value.Protrait);
                        }
                    }
                }
                if (!playerSurNameOverride.Value.Trim().IsNullOrEmpty())
                {
                    playerExteriorData.SurName = playerSurNameOverride.Value.Trim();
                }
                if (!playerNameOverride.Value.Trim().IsNullOrEmpty())
                {
                    playerExteriorData.Name = playerNameOverride.Value.Trim();
                }
            }
            Console.WriteLine("ReplacePlayerExteriorData end");
        }
        //EnterGame之后会直接开始游戏，创建playerEntity，之后再执行InitialRewards。在InitialRewards后再替换模型就晚了一些
        [HarmonyPrefix, HarmonyPatch(typeof(UIRegistration), "EnterGame")]
        public static bool StartPatch_SetPlayerModel()
        {
            ReplacePlayerExteriorData();
            return true;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(PlayerTemplate), "BuildEntity")]
        public static bool PlayerTemplatePatch_BuildEntity()
        {
            ReplacePlayerExteriorData();
            return true;
        }
        [HarmonyPostfix, HarmonyPatch(typeof(ChangeCharacterProtraitAndModel), "GetValue")]
        public static void StartPatch_SetPlayerModel2(ChangeCharacterProtraitAndModel __instance, bool __result)
        {
            if (__instance.id == GameConfig.Player)
            {
                ReplacePlayerExteriorData();
            }
        }
        [HarmonyPostfix, HarmonyPatch(typeof(ChangeCharacterIdentity), "GetValue")]
        public static void StartPatch_SetPlayerModel3(ChangeCharacterIdentity __instance, bool __result)
        {
            if (__instance.id == GameConfig.Player && __result)
            {
                ReplacePlayerExteriorData();
            }
        }

        /*[HarmonyPrefix, HarmonyPatch(typeof(UIRegistration), "UpdateView")]
        public static bool UIRegistrationPatch_UpdateView(UIRegistration __instance, ref RegistrationInfo _info)
        {
            _info.SurName = newGameSurNameOverride.Value.IsNullOrEmpty()? "亦": newGameSurNameOverride.Value;
            _info.Name = newGameNameOverride.Value.IsNullOrEmpty() ? "天凛" : newGameNameOverride.Value;
            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIRegistration), "UpdateName")]
        public static bool UIRegistrationPatch_UpdateName(UIRegistration __instance, ref string surName, ref string Name)
        {
            surName = newGameSurNameOverride.Value;
            Name = newGameNameOverride.Value;
            return true;
        }*/

        [HarmonyPostfix, HarmonyPatch(typeof(GameData), "Initialize")]
        public static void GameDataPatch_Initialize(GameData __instance)
        {
            Console.WriteLine("SteamPlatformPatch_LoadFileAsync start");
            playerSurNameOverride.SettingChanged -= ReplacePlayerExteriorDataEventHander;
            playerNameOverride.SettingChanged -= ReplacePlayerExteriorDataEventHander;

            playerSurNameOverride.Value = __instance.Exterior["Player"].SurName;
            playerNameOverride.Value = __instance.Exterior["Player"].Name;

            playerSurNameOverride.SettingChanged += ReplacePlayerExteriorDataEventHander;
            playerNameOverride.SettingChanged += ReplacePlayerExteriorDataEventHander;
            Console.WriteLine("SteamPlatformPatch_LoadFileAsync end");
        }

        [HarmonyPostfix, HarmonyPatch(typeof(CtrlRegistration), "SetLastName")]
        public static void CtrlRegistrationPatch_SetLastName(CtrlRegistration __instance, ref string value)
        {
            playerSurNameOverride.Value = value;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(CtrlRegistration), "SetFitstName")]
        public static void CtrlRegistrationPatch_SetFitstName(CtrlRegistration __instance, ref string value)
        {
            playerNameOverride.Value = value;
        }

        // 5 防止人物模型动作出现问题
        [HarmonyPrefix, HarmonyPatch(typeof(Heluo.Actor.ActorController), "OverrideStand", new Type[] { typeof(string) })]
        public static bool StartPatch_SetPlayerModel4(Heluo.Actor.ActorController __instance, ref string clipName)
        {
            AnimationClip animationClip = __instance.GetAnimationClip(clipName);
            if (animationClip == null)
            {
                int index = clipName.IndexOf("_special_await");
                if (index >= 0)
                {
                    Console.WriteLine("OverrideStand = " + clipName);
                    string ModelName = clipName.Substring(0, index);
                    var collection = from ce in Game.Data.Get<CharacterExterior>().Values where ce.Model == ModelName select ce;
                    if (collection.Count() > 0)
                    {
                        var characterExterior = collection.First();
                        if (!characterExterior.AnimMapId.IsNullOrEmpty())
                        {
                            clipName = string.Format("{0}_special_await{1:00}", characterExterior.AnimMapId, 0);
                            animationClip = __instance.GetAnimationClip(clipName);
                            if (animationClip == null)
                            {
                                var animMap = Game.Data.Get<AnimationMapping>(characterExterior.AnimMapId);
                                foreach (var (state, clip) in animMap)
                                {
                                    if (!clip.IsNullOrEmpty() && __instance.GetAnimationClip(clip) != null)
                                    {
                                        clipName = clip;
                                        break;
                                    }
                                }
                            }
                        }
                        animationClip = __instance.GetAnimationClip(clipName);
                        if (animationClip == null)
                            clipName = characterExterior.Gender == Gender.Male ? "in0101_special_await00" : "in0115_special_await00";
                    }
                }
            }
            return true;
        }
    }
}
