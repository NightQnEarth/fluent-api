﻿using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ObjectPrinting.Tests.TestTypes
{
    // "get" used implicitly when calling PrintToString() for instance in PrintToStringTests class.
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public class TypeWithCollectionOfCollections
    {
        public int[][] ArrayOfArrays { get; set; }
        public List<List<string>> ListOfLists { get; set; }
        public Dictionary<string, Dictionary<string, int>> DictionaryByString { get; set; }
    }
}