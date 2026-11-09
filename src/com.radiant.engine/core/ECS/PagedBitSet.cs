using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace com.radiant.engine.core;

/// <summary>
/// Ultra-fast paged bitset for tracking entity active state.
/// Uses 1 bit per entity instead of ~24+ bytes for HashSet entry.
/// Power-of-2 sizes enable bit operations instead of division.
/// </summary>
public sealed class PagedBitSet
{
    // 4096 ulongs per page = 262,144 bits per page = 32KB per page (L1 cache friendly)
    private const int PageSize = 4096;
    private const int BitsPerUlong = 64;
    private const int BitShift = 6;
    private const int BitMask = BitsPerUlong - 1;
    private const int BitsPerPage = PageSize * BitsPerUlong; // 262,144 entities per page

    private ulong[][] Pages;
    private int PageCount;
    private int EntityCount;

    public int Count => EntityCount;

    /// <summary>Allocates the initial page lookup for sparse entity membership.</summary>
    public PagedBitSet(int initialCapacity = 65536)
    {
        int requiredPages = Math.Max(1, (initialCapacity + BitsPerPage - 1) / BitsPerPage);
        Pages = new ulong[requiredPages][];
        PageCount = 0;
    }

    /// <summary>Allocates enough storage to represent the requested entity ID.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int entityId)
    {
        int pageIndex = entityId / BitsPerPage;

        if (pageIndex >= Pages.Length)
        {
            int newSize = Math.Max(Pages.Length * 2, pageIndex + 1);
            Array.Resize(ref Pages, newSize);
        }

        while (PageCount <= pageIndex)
        {
            Pages[PageCount++] = new ulong[PageSize];
        }
    }

    /// <summary>Reports whether the entity's membership bit is set.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int entityId)
    {
        if (entityId < 0) return false;

        int pageIndex = entityId / BitsPerPage;

        if (pageIndex >= PageCount) return false;

        var page = Pages[pageIndex];

        if (page == null) return false;

        int localBit = entityId % BitsPerPage;
        int ulongIndex = localBit >> BitShift;
        int bitIndex = localBit & BitMask;

        return (page[ulongIndex] & (1UL << bitIndex)) != 0;
    }

    /// <summary>Sets the entity's membership bit, allocating its page if needed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Add(int entityId)
    {
        if (entityId < 0) return false;

        EnsureCapacity(entityId);

        int pageIndex = entityId / BitsPerPage;
        int localBit = entityId % BitsPerPage;
        int ulongIndex = localBit >> BitShift;
        int bitIndex = localBit & BitMask;
        ulong mask = 1UL << bitIndex;

        ref ulong slot = ref Pages[pageIndex][ulongIndex];

        if ((slot & mask) != 0) return false;

        slot |= mask;
        EntityCount++;

        return true;
    }

    /// <summary>Clears the entity's membership bit when its page exists.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(int entityId)
    {
        if (entityId < 0) return false;

        int pageIndex = entityId / BitsPerPage;

        if (pageIndex >= PageCount) return false;

        var page = Pages[pageIndex];

        if (page == null) return false;

        int localBit = entityId % BitsPerPage;
        int ulongIndex = localBit >> BitShift;
        int bitIndex = localBit & BitMask;
        ulong mask = 1UL << bitIndex;

        ref ulong slot = ref page[ulongIndex];

        if ((slot & mask) == 0) return false;

        slot &= ~mask;
        EntityCount--;

        return true;
    }

    /// <summary>Clears all allocated membership pages.</summary>
    public void Clear()
    {
        for (int i = 0; i < PageCount; i++)
        {
            if (Pages[i] != null) Array.Clear(Pages[i], 0, PageSize);
        }

        EntityCount = 0;
    }

    /// <summary>
    /// Iterate all set bits. Yields entity IDs.
    /// </summary>
    public EntityEnumerator GetEnumerator() => new EntityEnumerator(this);

    /// <summary>Iterates the set entity IDs in a paged bit set.</summary>
    public ref struct EntityEnumerator
    {
        private readonly PagedBitSet Bitset;
        private int PageIdx;
        private int UlongIdx;
        private ulong CurrentBits;
        private int CurrentEntity;

        /// <summary>Starts iteration over the supplied membership pages.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal EntityEnumerator(PagedBitSet bitset)
        {
            Bitset = bitset;
            PageIdx = 0;
            UlongIdx = -1;
            CurrentBits = 0;
            CurrentEntity = -1;
        }

        /// <summary>Advances to the next set entity ID and reports whether one was found.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true)
            {
                if (CurrentBits != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(CurrentBits);
                    CurrentBits &= CurrentBits - 1;
                    CurrentEntity = PageIdx * BitsPerPage + UlongIdx * BitsPerUlong + bit;

                    return true;
                }

                UlongIdx++;

                while (UlongIdx >= PageSize || (PageIdx < Bitset.PageCount && Bitset.Pages[PageIdx] == null))
                {
                    PageIdx++;
                    UlongIdx = 0;

                    if (PageIdx >= Bitset.PageCount) return false;
                }

                if (PageIdx >= Bitset.PageCount) return false;

                CurrentBits = Bitset.Pages[PageIdx][UlongIdx];
            }
        }

        public int Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => CurrentEntity;
        }
    }
}
