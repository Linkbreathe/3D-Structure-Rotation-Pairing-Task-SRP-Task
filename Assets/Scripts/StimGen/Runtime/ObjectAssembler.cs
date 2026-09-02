using System.Collections.Generic;
using UnityEngine;

namespace StimGen
{
    /// <summary>把 ObjectDefinition 实例化成场景里的 GameObject（Root + 8 个零件）。</summary>
    public static class ObjectAssembler
    {
        /// <summary>
        /// 生成一个刺激物体。层级与场景里已有的手搭原型一致：
        /// 一个根 GameObject 承载 8 个零件子物体，旋转只作用在根上。
        /// </summary>
        public static StimulusObject Build(ObjectDefinition def, Transform parent = null)
        {
            var rootGo = new GameObject(def.objectId);
            if (parent != null) rootGo.transform.SetParent(parent, false);

            var stim = rootGo.AddComponent<StimulusObject>();
            stim.definition = def;
            stim.parts = new List<PartTag>(def.parts.Count);

            for (int i = 0; i < def.parts.Count; i++)
            {
                GameObject partGo = PartLibrary.CreatePart(def.parts[i], rootGo.transform);
                stim.parts.Add(partGo.GetComponent<PartTag>());
            }
            return stim;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }
    }

    /// <summary>场景里一个已实例化的刺激物体。旋转永远只改根节点。</summary>
    public class StimulusObject : MonoBehaviour
    {
        public ObjectDefinition definition;
        public List<PartTag> parts = new List<PartTag>();

        /// <summary>设置整体朝向。零件之间的相对关系永远不变。</summary>
        public void SetRotation(Quaternion rotation)
        {
            transform.rotation = rotation;
        }

        public void SetYaw(float degrees)
        {
            transform.rotation = Quaternion.Euler(0f, degrees, 0f);
        }
    }
}
