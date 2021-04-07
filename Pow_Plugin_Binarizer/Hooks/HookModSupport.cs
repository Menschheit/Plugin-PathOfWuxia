﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Timers;
using HarmonyLib;
using UnityEngine;
using BepInEx;
using BepInEx.Configuration;
using Heluo;
using Heluo.UI;
using Heluo.FSM;
using Heluo.FSM.Main;
using Heluo.Data;
using Heluo.Flow;
using Heluo.Battle;
using Heluo.Resource;
using Heluo.Utility;
using FileHelpers;
using Newtonsoft.Json;

namespace PathOfWuxia
{
    // Mod支持
    public class HookModSupport : IHook
    {
        public void OnRegister(BaseUnityPlugin plugin)
        {
            modPath = plugin.Config.Bind("Mod设置", "Mod路径", "", "该项必须启动前从设置中修改才可生效");
            modTheme = plugin.Config.Bind("Mod设置", "Mod主菜单音乐", "", "下次进入主菜单生效");
            modCustomVoice = plugin.Config.Bind("Mod设置", "Mod语音开关", false, "吧友配音");
            modBattleVoicePath = plugin.Config.Bind("Mod设置", "Mod战斗语音路径", "audio/voice/um_{0}_{1}.ogg", "可更改相对路径和扩展名");
            modTalkVoicePath = plugin.Config.Bind("Mod设置", "Mod对话语音路径", "audio/voice/talk_{0}.ogg", "可更改相对路径和扩展名");
        }

        public void OnUpdate()
        {
        }

        static ConfigEntry<string> modPath;
        static ConfigEntry<string> modTheme;
        static ConfigEntry<bool> modCustomVoice;
        static ConfigEntry<string> modBattleVoicePath;
        static ConfigEntry<string> modTalkVoicePath;

        // 最简单的方式：更改ExternalResourceProvider的外部路径，但无法加载音乐
        // 尝试过挂接泛型函数T Load<T>(string)，但不好用，他总会使用最后一个类的泛型注入导致其他类型无法读取，为Harmony固有问题。
        //[HarmonyPostfix, HarmonyPatch(typeof(ExternalResourceProvider), MethodType.Constructor, new Type[] { typeof(ICoroutineRunner), typeof(Heluo.Mod.IModManager) })]
        //public static void ModPatch_Constructor(ExternalResourceProvider __instance)
        //{
        //    Traverse.Create(__instance).Field("ExternalDirectory").SetValue(modPath.Value);
        //}

        // 更换 ExternalResourceProvider 为 ModResourceProvider
        [HarmonyPostfix, HarmonyPatch(typeof(ResourceManager), "Reset", new Type[] { typeof(ICoroutineRunner), typeof(Heluo.Mod.IModManager), typeof(Type[]) })]
        public static void ModPatch_Reset(ResourceManager __instance, ICoroutineRunner runner)
        {
            if (!modPath.Value.IsNullOrEmpty() && Directory.Exists(Path.GetFullPath(modPath.Value)))
            {
                var provider = Traverse.Create(__instance).Field("provider").GetValue<IChainedResourceProvider>();
                var thirdSuccessor = provider.Successor.Successor;
                var modResourceProvider = new ModResourceProvider(runner, modPath.Value);
                provider.Successor = modResourceProvider;
                modResourceProvider.Successor = thirdSuccessor;

                Console.WriteLine("当前ResourceProvider链表: ");
                while (provider != null)
                {
                    Console.WriteLine(provider.GetType().ToString());
                    provider = provider.Successor;
                }
            }
        }

        // 1 Mod支持增删表格
        [HarmonyPrefix, HarmonyPatch(typeof(DataManager), "ReadData", new Type[] { typeof(string) })]
        public static bool ModPatch_ReadData(ref DataManager __instance, string path)
        {
            var dict = Traverse.Create(__instance).Field("dict");
            var resource = Traverse.Create(__instance).Field("resource").GetValue<IResourceProvider>();
            path = __instance.CheckPath(path);
            dict.SetValue(new Dictionary<Type, IDictionary>());
            Type type = typeof(Item);
            foreach (Type itemType in from t in type.Assembly.GetTypes()
                                      where t.IsSubclassOf(type) && !t.HasAttribute<Hidden>(false)
                                      select t)
            {
                Type typeItemDic = typeof(CsvDataSource<>).MakeGenericType(new Type[]
                {
                    itemType
                });
                try
                {
                    byte[] fileData = null;
                    IDictionary itemDic;
                    if (!itemType.HasAttribute<JsonConfig>())
                    {
                        fileData = resource.LoadBytes(path + itemType.Name + ".txt");
                    }
                    if (fileData != null)
                    {
                        // 主数据 *.txt
                        itemDic = (Activator.CreateInstance(typeItemDic, new object[] { fileData }) as IDictionary);

                        // 补充数据 *_modify.txt
                        byte[] fileDataModify = resource.LoadBytes(path + itemType.Name + "_modify.txt");
                        if (fileDataModify != null)
                        {
                            IDictionary dicModify = (Activator.CreateInstance(typeItemDic, new object[]
                            {
                                fileDataModify
                            }) as IDictionary);
                            foreach (var key in dicModify.Keys)
                            {
                                if (itemDic.Contains(key))
                                    itemDic[key] = dicModify[key];
                                else
                                    itemDic.Add(key, dicModify[key]);
                            }
                        }
                        // 删除数据 *_remove.txt
                        byte[] fileDataRemove = resource.LoadBytes(path + itemType.Name + "_remove.txt");
                        if (fileDataRemove != null)
                        {
                            IDictionary dicRemove = (Activator.CreateInstance(typeItemDic, new object[]
                            {
                                fileDataRemove
                            }) as IDictionary);
                            foreach (var key in dicRemove.Keys)
                            {
                                if (itemDic.Contains(key))
                                    itemDic.Remove(key);
                            }
                        }
                    }
                    else
                    {
                        // json文件 *.json
                        byte[] jsonData = resource.LoadBytes("Config/" + itemType.Name + ".json");
                        if (jsonData != null)
                        {
                            string @string = Encoding.UTF8.GetString(jsonData);
                            typeItemDic = typeof(Dictionary<,>).MakeGenericType(new Type[]
                            {
                            typeof(string),
                            itemType
                            });
                            itemDic = (JsonConvert.DeserializeObject(@string, typeItemDic) as IDictionary);
                        }
                        else
                        {
                            // 没有对应文件
                            Debug.Log("没找到该类型的数据：" + itemType.ToString());
                            continue;
                        }
                    }
                    dict.GetValue<IDictionary>().Add(itemType, itemDic);
                }
                catch (ConvertException ex)
                {
                    Debug.LogError(string.Concat(new object[]
                    {
                        "解析 ",
                        itemType.Name,
                        " 時發生錯誤 !!\r\n行數 : ",
                        ex.LineNumber,
                        ", 欄位 : ",
                        ex.ColumnNumber,
                        ", 類型 = ",
                        ex.FieldType.Name,
                        ", 名稱 = ",
                        ex.FieldName,
                        "\r\n",
                        ex
                    }));
                }
                catch (Exception ex2)
                {
                    Debug.LogError(string.Concat(new object[]
                    {
                        "解析 ",
                        itemType.Name,
                        " 時發生錯誤 !!\r\n",
                        ex2
                    }));
                }
            }
            return false;
        }

        // 2 修改主题音乐
        [HarmonyPrefix, HarmonyPatch(typeof(MusicPlayer), "ChangeMusic", new Type[] { typeof(string), typeof(float), typeof(float), typeof(bool), typeof(bool), typeof(bool) })]
        public static bool ModPatch_ChangeTheme(ref string _name)
        {
            if (_name == "In_theme_01.wav" && modTheme.Value != string.Empty)
                _name = modTheme.Value;
            return true;
        }

        // 3 玩家自定义配音-战斗
        private static Dictionary<string, List<AudioClip>> _battleVoices = new Dictionary<string, List<AudioClip>>();
        private static System.Timers.Timer _voiceTimer;

        public static void PlayCustomizedVoice(AudioClip clip)
        {
            AudioSource ss = Traverse.Create(Game.MusicPlayer).Field("single_source").GetValue<AudioSource>();
            var currVol = Traverse.Create(Game.MusicPlayer).Field("current_volume_percent");
            if (ss == null)
            {
                return;
            }
            ss.Stop();
            ss.spatialBlend = 0f;
            ss.gameObject.transform.localPosition = Vector3.zero;
            ss.volume = GameConfig.SoundVolume;
            ss.PlayOneShot(clip);
            currVol.SetValue(0.2f);
            Game.MusicPlayer.SetVolume();
            if (_voiceTimer == null)
            {
                _voiceTimer = new System.Timers.Timer();
                _voiceTimer.Elapsed += delegate (object source, ElapsedEventArgs e)
                {
                    currVol.SetValue(1f);
                    Game.MusicPlayer.SetVolume();
                };
            }
            _voiceTimer.Stop();
            _voiceTimer.Interval = (double)(clip.length * 1000f);
            _voiceTimer.Start();
        }
        public static void PlayCvByCharacter(string id)
        {
            List<AudioClip> list;
            if (!_battleVoices.ContainsKey(id))
            {
                list = new List<AudioClip>();
                for (int i = 0; i < 5; i++)
                {
                    AudioClip audioClip = Game.Resource.Load<AudioClip>(string.Format(modBattleVoicePath.Value, id, i));
                    if (audioClip != null)
                    {
                        list.Add(audioClip);
                    }
                }
                _battleVoices.Add(id, list);
            }
            else
            {
                list = _battleVoices[id];
            }
            if (list.Count > 0)
            {
                PlayCustomizedVoice(list[UnityEngine.Random.Range(0, list.Count)]);
            }
        }
        [HarmonyPostfix, HarmonyPatch(typeof(UIBattle), "OpenBattleStatus", new Type[] { typeof(WuxiaUnit) })]
        public static void ModPatch_BattleVoice(WuxiaUnit _unit)
        {
            if (modCustomVoice.Value)
            {
                PlayCvByCharacter(_unit.ExteriorId);
            }
        }
        // 4 玩家自定义配音-对话
        public static bool PlayCvByPath(string soundPath)
        {
            AudioClip audioClip = Game.Resource.Load<AudioClip>(soundPath);
            if (audioClip == null)
            {
                return false;
            }
            PlayCustomizedVoice(audioClip);
            return true;
        }
        [HarmonyPostfix, HarmonyPatch(typeof(CtrlTalk), "SetMessageView", new Type[] { typeof(Talk) })]
        public static void ModPatch_TalkVoice(Talk talk)
        {
            if (modCustomVoice.Value)
            {
                PlayCvByPath(string.Format(modTalkVoicePath.Value, talk.Id));
            }
        }

        // 5 进战斗无需填写场景名；可随机场景
        private static string CachedBattleId = "";
        [HarmonyPrefix, HarmonyPatch(typeof(BattleAction), "GetValue")]
        public static bool ModPatch_BattleAction(BattleAction __instance, ref bool __result)
        {
            if (!Application.isPlaying || __instance.battleId.IsNullOrEmpty())
            {
                __result = false;
                return false;
            }
            string battleId = __instance.battleId;
            BattleArea battleArea = Game.Data.Get<BattleArea>(battleId);
            BattleGrid battleGrid = Game.Data.Get<BattleGrid>(battleArea?.BattleMap);
            if (battleGrid == null)
            {
                battleGrid = Randomizer.GetOneFromData<BattleGrid>(battleArea?.BattleMap);
                if (battleGrid != null)
                {
                    BattleArea battleAreaClone = battleArea.Clone<BattleArea>();
                    battleAreaClone.Id = "!" + battleAreaClone.Id;
                    battleAreaClone.BattleMap = battleGrid.Id;  // 复写mapId
                    ModExtensionSaveData.AddTempItem(battleAreaClone);
                    battleId = battleAreaClone.Id;
                }
            }
            string mapId = battleGrid?.MapId;
            Console.WriteLine("当前MapId="+ Game.GameData.MapId);
            Console.WriteLine("需要MapId="+ mapId);
            if (mapId == Game.GameData.MapId)
            {
                Game.FSM.SendEvent("BATTLE", new Heluo.FSM.Main.BattleEventArgs() { BattleId = battleId });
                __result = true;
            }
            else
            {
                CachedBattleId = battleId;
                Console.WriteLine("设置BattleId=" + CachedBattleId);
                Game.FSM.SendEvent("LOADING", new LoadingEventArgs
                {
                    MapId = mapId,
                    CinematicId = null,
                    TimeStage = Heluo.Manager.TimeStage.None,
                    LoadType = LoadType.Default
                });
                __result = true;
            }
            return false;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(GameState<MainStateMachine>), "SendEvent", new Type[] { typeof(string), typeof(EventArgs) })]
        public static bool ModPatch_LoadBattlePost(GameState<MainStateMachine> __instance, ref string eventName, ref EventArgs e)
        {
            Console.WriteLine("当前BattleId=" + CachedBattleId);
            Console.WriteLine("eventName=" + eventName);
            Console.WriteLine("e=" + e);
            Console.WriteLine("当前MapId=" + Game.GameData.MapId);
            if (!CachedBattleId.IsNullOrEmpty() && eventName == "CINEMATIC" && e==null)
            {
                eventName = "BATTLE";
                e = new Heluo.FSM.Main.BattleEventArgs() { BattleId = CachedBattleId };
                CachedBattleId = "";
            }
            return true;
        }
    }
}
