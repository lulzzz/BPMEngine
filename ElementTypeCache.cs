﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Org.Reddragonit.BpmEngine
{
    internal class ElementTypeCache
    {
        private Dictionary<string, Type> _cachedMaps;

        public ElementTypeCache() { _cachedMaps = new Dictionary<string, Type>(); }

        public Type this[string xmlTag]
        {
            get
            {
                if (_cachedMaps.ContainsKey(xmlTag.ToLower()))
                    return _cachedMaps[xmlTag.ToLower()];
                return null;
            }
            set
            {
                _cachedMaps.Add(xmlTag.ToLower(), value);
            }
        }

        public bool IsCached(string xmlTag)
        {
            return _cachedMaps.ContainsKey(xmlTag.ToLower());
        }

        public void MapIdeals(XmlPrefixMap map)
        {
            Dictionary<string, Dictionary<string, Type>> ideals = Utility.IdealMap;
            foreach (string prefix in ideals.Keys)
            {
                List<string> tmp = map.Translate(prefix);
                if (tmp.Count > 0)
                {
                    foreach (string trans in tmp)
                    {
                        foreach (string tag in ideals[prefix].Keys)
                        {
                            if (!_cachedMaps.ContainsKey(string.Format("{0}:{1}", trans, tag)))
                                _cachedMaps.Add(string.Format("{0}:{1}", trans, tag), ideals[prefix][tag]);
                        }
                    }
                }
            }
        }
    }
}
