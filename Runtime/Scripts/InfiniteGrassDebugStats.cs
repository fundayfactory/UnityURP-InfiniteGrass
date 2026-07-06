using System.Text;
using UnityEngine;

namespace InfiniteGrass
{
    public sealed class InfiniteGrassDebugStats : MonoBehaviour
    {
        private readonly StringBuilder _stringBuilder = new();
        private GUIStyle _style;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;
            
            _style ??= new GUIStyle { fontSize = 18, normal = { textColor = Color.darkOrange } };
            var added = 0;
            
            for (var i = 0; i < InfiniteGrassUtility.Settings.Count; i++)
            {
                if (!InfiniteGrassUtility.Settings[i].previewVisibleGrassCount)
                    continue;

                added++;
                _stringBuilder.Clear();

                var obj = Resources.EntityIdToObject(InfiniteGrassUtility.EntityIds[i]);
                _stringBuilder.Append(obj != null ? obj.name : "Unknown");
                _stringBuilder.Append(": ");
                
                // Reading back data from GPU
                var count = new uint[5];
                InfiniteGrassUtility.ArgsBuffers[i].GetData(count);
                _stringBuilder.AppendFormat("{0}", count[1]);
                
                GUI.Label(new Rect(64, 64 + 32 * added, 400, 32), _stringBuilder.ToString(), _style);
            }
        }
    }
}