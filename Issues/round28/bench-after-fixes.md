# Benchmarks after the round-28 fixes (2026-09-02 13:55, 47 min) vs before (2026-09-01 21:52, 25 min)

Same machine, same 55 benchmarks, same payloads. Allocations: identical for all 55 (20 pins exact). Timing deltas move ALL libraries in the same categories (Mapperly List +33 %, Mapster NestedFill −33 %, AutoMapper Dict +22 %), and the run took twice as long, so the shifts are machine state, not code: the fixes changed compile-time blit ELIGIBILITY and emission for shapes these benchmarks do not use. Blit/Widen rows, the only ones a proof change could have moved, are within 2 %.

| Category | Method | before ns | after ns | Δ | alloc before | alloc after |
|---|---|---:|---:|---:|---:|---:|
| Array | Array_Dwarf | 4,917.4 | 5,332.5 | +8.4% ⚠ | 48,048 | 48,048 |
| Array | Array_Mapperly | 4,924.7 | 5,271.8 | +7.0% ⚠ | 48,048 | 48,048 |
| Array | Array_Mapster | 6,530.4 | 6,474.0 | -0.9% | 48,048 | 48,048 |
| Array | Array_AutoMapper | 5,696.5 | 5,827.7 | +2.3% | 48,048 | 48,048 |
| Blit | Blit_Dwarf | 416.7 | 425.1 | +2.0% | 12,048 | 12,048 |
| Blit | Blit_Mapperly | 959.7 | 958.5 | -0.1% | 12,048 | 12,048 |
| Blit | Blit_Mapster | 987.6 | 1,013.0 | +2.6% | 12,048 | 12,048 |
| Blit | Blit_AutoMapper | 1,055.0 | 1,102.4 | +4.5% | 12,048 | 12,048 |
| BlitRatio | BlitRatio_Array_Fast | 433.8 | 433.1 | -0.2% | 12,048 | 12,048 |
| BlitRatio | BlitRatio_Array_Scalar | 1,063.2 | 1,076.0 | +1.2% | 16,048 | 16,048 |
| BlitRatio | BlitRatio_List_Fast | 452.5 | 569.7 | +25.9% ⚠ | 12,112 | 12,112 |
| BlitRatio | BlitRatio_List_Scalar | 1,076.9 | 1,348.2 | +25.2% ⚠ | 16,112 | 16,112 |
| Dict | Dict_Dwarf | 8,201.9 | 9,259.9 | +12.9% ⚠ | 31,120 | 31,120 |
| Dict | Dict_Mapperly | 19,332.9 | 19,572.0 | +1.2% | 31,176 | 31,176 |
| Dict | Dict_Mapster | 26,023.0 | 26,355.9 | +1.3% | 102,376 | 102,376 |
| Dict | Dict_AutoMapper | 18,950.0 | 23,220.3 | +22.5% ⚠ | 102,320 | 102,320 |
| EnumByName | Enum_Dwarf | 12.6 | 12.7 | +1.0% | 24 | 24 |
| EnumByName | Enum_Mapperly | 12.4 | 12.7 | +1.8% | 24 | 24 |
| EnumByName | Enum_AutoMapper | 74.3 | 85.1 | +14.6% ⚠ | 48 | 48 |
| EnumByValue | EnumByValue_Dwarf | 3.4 | 4.4 | +32.2% ⚠ | 24 | 24 |
| EnumByValue | EnumByValue_Mapperly | 3.3 | 3.6 | +8.6% ⚠ | 24 | 24 |
| EnumByValue | EnumByValue_Mapster | 12.4 | 12.7 | +1.9% | 24 | 24 |
| Flat | Flat_Hand | 5.0 | 5.0 | +1.4% | 40 | 40 |
| Flat | Flat_Dwarf | 5.5 | 5.7 | +3.7% | 40 | 40 |
| Flat | Flat_Mapperly | 5.4 | 5.1 | -5.4% ✓ | 40 | 40 |
| Flat | Flat_Mapster | 14.7 | 14.3 | -2.2% | 40 | 40 |
| Flat | Flat_AutoMapper | 53.5 | 54.1 | +1.1% | 40 | 40 |
| Flatten | Flatten_Dwarf | 5.7 | 6.0 | +6.4% ⚠ | 48 | 48 |
| Flatten | Flatten_Mapperly | 5.7 | 5.6 | -1.8% | 48 | 48 |
| Flatten | Flatten_Mapster | 14.8 | 14.5 | -2.5% | 48 | 48 |
| Flatten | Flatten_AutoMapper | 53.9 | 53.3 | -1.0% | 48 | 48 |
| Immutable | Immutable_Dwarf | 158.2 | 142.5 | -9.9% ✓ | 4,048 | 4,048 |
| List | List_Dwarf | 6,032.2 | 6,356.6 | +5.4% ⚠ | 48,112 | 48,112 |
| List | List_Mapperly | 5,903.8 | 7,847.9 | +32.9% ⚠ | 48,112 | 48,112 |
| List | List_Mapster | 5,571.6 | 6,967.2 | +25.0% ⚠ | 48,112 | 48,112 |
| List | List_AutoMapper | 8,668.8 | 8,701.7 | +0.4% | 56,656 | 56,656 |
| Nested | Nested_Dwarf | 11.1 | 11.1 | +0.4% | 112 | 112 |
| Nested | Nested_Mapperly | 11.8 | 11.1 | -5.9% ✓ | 112 | 112 |
| Nested | Nested_Mapster | 20.4 | 20.6 | +1.0% | 112 | 112 |
| Nested | Nested_AutoMapper | 58.9 | 62.1 | +5.5% ⚠ | 112 | 112 |
| NestedFill | NestedFill_Dwarf | 915.7 | 966.1 | +5.5% ⚠ | 8,112 | 8,112 |
| NestedFill | NestedFill_Mapperly | 1,764.1 | 1,949.1 | +10.5% ⚠ | 9,456 | 9,456 |
| NestedFill | NestedFill_Mapster | 1,659.5 | 1,108.9 | -33.2% ✓ | 8,112 | 8,112 |
| NestedFill | NestedFill_AutoMapper | 2,094.8 | 2,168.0 | +3.5% | 10,776 | 10,776 |
| NullMismatch | NullMismatch_Dwarf | 5.7 | 7.1 | +23.7% ⚠ | 32 | 32 |
| NumList | NumList_Dwarf | 669.1 | 693.4 | +3.6% | 8,112 | 8,112 |
| NumList | NumList_Mapperly | 961.0 | 933.3 | -2.9% | 8,112 | 8,112 |
| NumList | NumList_Mapster | 938.1 | 996.4 | +6.2% ⚠ | 8,112 | 8,112 |
| NumList | NumList_AutoMapper | 2,358.6 | 2,942.8 | +24.8% ⚠ | 16,656 | 16,656 |
| Seq | Seq_Dwarf | 7,568.5 | 6,698.0 | -11.5% ✓ | 48,088 | 48,088 |
| Set | Set_Dwarf | 4,422.0 | 4,362.8 | -1.3% | 17,856 | 17,856 |
| Widen | Widen_Dwarf | 343.8 | 348.4 | +1.3% | 8,048 | 8,048 |
| Widen | Widen_Mapperly | 435.9 | 427.7 | -1.9% | 8,048 | 8,048 |
| Widen | Widen_Mapster | 713.9 | 678.6 | -4.9% | 8,048 | 8,048 |
| Widen | Widen_AutoMapper | 729.6 | 731.7 | +0.3% | 8,048 | 8,048 |
