#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

[CustomEditor(typeof(MonsterConfig))]
public class MonsterConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 绘制默认Inspector界面
        DrawDefaultInspector();

        // 在Inspector下方添加导出/导入按钮
        MonsterConfig config = (MonsterConfig)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("配置导入/导出", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("导出配置JSON"))
        {
            string path = EditorUtility.SaveFilePanel("导出怪物配置JSON", "", config.monsterID + ".json", "json");
            if (!string.IsNullOrEmpty(path))
            {
                // 序列化为JSON字符串并写入文件
                string json = JsonUtility.ToJson(config, true);
                File.WriteAllText(path, json);
                Debug.Log("怪物配置已导出到: " + path);
            }
        }
        if (GUILayout.Button("从JSON导入配置"))
        {
            string path = EditorUtility.OpenFilePanel("导入怪物配置JSON", "", "json");
            if (!string.IsNullOrEmpty(path))
            {
                string json = File.ReadAllText(path);
                // 从JSON覆盖当前对象字段
                JsonUtility.FromJsonOverwrite(json, config);
                EditorUtility.SetDirty(config);
                Debug.Log("已从JSON导入怪物配置: " + path);
            }
        }
        EditorGUILayout.EndHorizontal();
    }
}
#endif