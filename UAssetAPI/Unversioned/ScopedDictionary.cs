using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace UAssetAPI.Unversioned
{
    /// <summary>
    /// A dictionary whose writes go to a scope over a fixed base: reads find the scope's entry first, then the base's.
    /// <see cref="Usmap.BeginAssetScope"/> uses it so that schemas and enums an asset contributes (its own class and
    /// struct exports, and those pulled from the packages it imports) last only while that asset is read, and can't
    /// replace another project's types of the same name in later assets read with the same mappings.
    /// </summary>
    public class ScopedDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        public readonly IDictionary<TKey, TValue> Base;
        public readonly Dictionary<TKey, TValue> Scope;

        public ScopedDictionary(IDictionary<TKey, TValue> baseDictionary)
        {
            Base = baseDictionary;
            IEqualityComparer<TKey> comparer = baseDictionary switch
            {
                Dictionary<TKey, TValue> d => d.Comparer,
                ConcurrentDictionary<TKey, TValue> c => c.Comparer,
                _ => EqualityComparer<TKey>.Default,
            };
            Scope = new Dictionary<TKey, TValue>(comparer);
        }

        public void ClearScope() => Scope.Clear();

        public TValue this[TKey key]
        {
            get => Scope.TryGetValue(key, out var value) ? value : Base[key];
            set => Scope[key] = value;
        }

        public bool TryGetValue(TKey key, out TValue value) => Scope.TryGetValue(key, out value) || Base.TryGetValue(key, out value);
        public bool ContainsKey(TKey key) => Scope.ContainsKey(key) || Base.ContainsKey(key);
        public void Add(TKey key, TValue value) => Scope.Add(key, value);
        public bool Remove(TKey key) => Scope.Remove(key);

        IEnumerable<KeyValuePair<TKey, TValue>> Union() => Scope.Concat(Base.Where(entry => !Scope.ContainsKey(entry.Key)));
        public ICollection<TKey> Keys => Union().Select(entry => entry.Key).ToList();
        public ICollection<TValue> Values => Union().Select(entry => entry.Value).ToList();
        public int Count => Base.Count + Scope.Keys.Count(key => !Base.ContainsKey(key));
        public bool IsReadOnly => false;

        public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
        public void Clear() => Scope.Clear();
        public bool Contains(KeyValuePair<TKey, TValue> item) => TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) { foreach (var entry in Union()) array[arrayIndex++] = entry; }
        public bool Remove(KeyValuePair<TKey, TValue> item) => Contains(item) && Remove(item.Key);
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => Union().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
