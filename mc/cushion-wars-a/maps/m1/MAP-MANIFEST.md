# M1 地图清单（生成物，请勿手改）

- 生成器：tools/gen_map.py v1.0.0
- 源数据：maps/m1/board.json（SHA-256 `2f6222d434405da5c0a045f37ff9a70f9d3333062dbea848f658317460668761`）
- 维度：`minecraft:overworld`
- 原点：棋盘中心地面块 (20000, 199, 20000)，站立/坐垫层 Y=200
- 施工框（绝对坐标）：X 19985..20015，Y 199..201，Z 19985..20015

## 数量核对

| 项目 | 实际 | 设计稿 |
|---|---:|---:|
| 地面格 | 961 | 961 |
| 掩体占地格 | 52 | 52 |
| 掩体方块 | 104 | 104 |
| 初始非掩体地面格 | 909 | 909 |

## 出生点

| 编号 | 阵营 | 相对 X | 相对 Z |
|---|---|---:|---:|
| red1 | red | -6 | -13 |
| red2 | red | -2 | -13 |
| red3 | red | 2 | -13 |
| red4 | red | 6 | -13 |
| blue1 | blue | 6 | 13 |
| blue2 | blue | 2 | 13 |
| blue3 | blue | -2 | 13 |
| blue4 | blue | -6 | 13 |

## 回合表（棋盘与最大生命）

| 回合 | 棋盘 | 最大生命 | 本回合缩圈 | 本回合预警 |
|---:|---:|---:|---|---|
| 1 | 31x31 | 40 | — | — |
| 2 | 31x31 | 40 | — | — |
| 3 | 31x31 | 40 | — | — |
| 4 | 31x31 | 40 | — | — |
| 5 | 31x31 | 40 | — | — |
| 6 | 31x31 | 40 | — | 是 |
| 7 | 29x29 | 40 | 是 | 是 |
| 8 | 27x27 | 40 | 是 | 是 |
| 9 | 25x25 | 40 | 是 | 是 |
| 10 | 23x23 | 40 | 是 | 是 |
| 11 | 21x21 | 40 | 是 | 是 |
| 12 | 19x19 | 36 | 是 | 是 |
| 13 | 17x17 | 32 | 是 | 是 |
| 14 | 15x15 | 28 | 是 | 是 |
| 15 | 13x13 | 24 | 是 | 是 |
| 16 | 11x11 | 20 | 是 | 是 |
| 17 | 9x9 | 16 | 是 | 是 |
| 18 | 7x7 | 12 | 是 | 是 |
| 19 | 5x5 | 8 | 是 | — |
| 20 | 5x5 | 4 | — | — |
| 21 | 5x5 | 2 | — | — |

## 生成文件

| 文件 | 字节 | SHA-256 |
|---|---:|---|
| `src/round-a/data/cushionwars_a/function/arena/gen/build_covers.mcfunction` | 1477 | `ab05d802c8578b0e118d7257286f8653afa1d957a8a0512755618f8b2c3c392d` |
| `src/round-a/data/cushionwars_a/function/arena/gen/build_ground.mcfunction` | 28209 | `f955cd35b6e3e147ebc893f35e20d11838f64055371de635b2aac7a79d55aca1` |
| `src/round-a/data/cushionwars_a/function/arena/gen/clear_map.mcfunction` | 343 | `174a4b94a82996ffec91aed440b387e908e4d39e62703a80b785e55c8ff7b719` |
| `src/round-a/data/cushionwars_a/function/arena/gen/forceload_add.mcfunction` | 314 | `9c52da00564078de364ee272be8fca5c687a62a3dbfa0e7f73b4e539df034449` |
| `src/round-a/data/cushionwars_a/function/arena/gen/forceload_remove.mcfunction` | 315 | `47af069f1d37391d6c0d0c971d92ececdaee600c7db5a1865e44aa4dce5413a7` |
| `src/round-a/data/cushionwars_a/function/arena/gen/place_spawn_cushions.mcfunction` | 1729 | `ff6b231fa27d15b369294a608aa2ba3a23d67363e5b86d55024b82b8c59d4ac0` |
| `src/round-a/data/cushionwars_a/function/arena/gen/preflight/all.mcfunction` | 426 | `03ff0c42f0e4a7e862d8bac116ce4ed20ead8dba9f301f5fa407fdd6752b1f39` |
| `src/round-a/data/cushionwars_a/function/arena/gen/preflight/y199.mcfunction` | 99258 | `b502f4f7a19ed4500c8d0c4d93cc72d249d60096fabbd9a28d0eee260f369ee5` |
| `src/round-a/data/cushionwars_a/function/arena/gen/preflight/y200.mcfunction` | 99258 | `69cbd9a842e8bdf9cd8a3757f428c4932748344a4388b8e4004e9c6c7605f55a` |
| `src/round-a/data/cushionwars_a/function/arena/gen/preflight/y201.mcfunction` | 99258 | `378452a132652f596a85a0b9af870af5e77f6cdc405099246cc0b4c672119396` |
| `src/round-a/data/cushionwars_a/function/arena/gen/selfcheck.mcfunction` | 124869 | `d39f8e727c3051bc32518e6c99810fec5cbeda35f4d4b105f7c2cc935a410bed` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r07.mcfunction` | 1726 | `a57677da90ea7c82923eb93bbd588a0f19b3e1c367afddf285fc8272a26aad0a` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r08.mcfunction` | 1726 | `741f16b5a2b8358c067e2b337969b0b04e6b7e7cfc7c5732d1d585263c97e2c7` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r09.mcfunction` | 1726 | `07aea00fa5438489fd483fe11bd8e50831445199702149221986dfac2111607e` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r10.mcfunction` | 1727 | `4a416c0db58ca7605b728d99eb92c7f29781abdff907ac5fddbc4682630178d6` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r11.mcfunction` | 1727 | `af16493a63bdfa31a2517452c49ab4b602ac24db782361060f8e89421fe8b6c6` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r12.mcfunction` | 1726 | `8aae8b95c0a49ec3ef7fbd2fb4c9b5106cd5b71882860019f9f65b9cd8ea3bac` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r13.mcfunction` | 1725 | `ee3b2a963bde6b5990aee78aa10c95b0ae39cfc58f36ca27a46a287e15911b5e` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r14.mcfunction` | 1725 | `028259ac6df075f959d72093afc2703efd748407abd07f9d3f603e1ddc375758` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r15.mcfunction` | 1725 | `574e521eef4f1248aa0f0bd703871bd14efac6356e84bb3b54b8424ffb4f5a09` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r16.mcfunction` | 1725 | `2bdb689207df1795a9921329178db98f4f01a8e3d35c2bd718aacb6797c227e2` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r17.mcfunction` | 1716 | `c8ac0bd3cd5e83983c45eb6c6e0563ed17b135050cb4177d43a09fe81be8419d` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r18.mcfunction` | 1708 | `8cdcd66c892572f3ecc9862e8c72bc605d2cae11d3e2e98573e38eff257e04e0` |
| `src/round-a/data/cushionwars_a/function/arena/gen/shrink/shrink_r19.mcfunction` | 1708 | `35c8b91ddc2141dfc58308251b76fe2fc79deabf7136853bdbdb0f597df133fc` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r06.mcfunction` | 8012 | `0007ccd66a8ded42587ab81acc456b1e36fb1cea11a2a14da721d8937b87cc14` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r07.mcfunction` | 7500 | `c50476ea2a7659930b12c038de7cb207fe4b262a0f42ec4fffc5711a6312d8d2` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r08.mcfunction` | 6988 | `28dead6c9e9df0508b1fde89bb67aaebf93844be0d0db1adf3ed333cea6acc3c` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r09.mcfunction` | 6477 | `6072e3abafa4d308ed42bb1a95fa2a0a8cb7b48d61a116d3f493d789e432b540` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r10.mcfunction` | 5966 | `b2eae36b8483aa75c3a67aa2d409ae35e71706e25aa759080f4c2cd1a95a654f` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r11.mcfunction` | 5454 | `27946fbaa84e72234259f39713aa9e45e6944734a10897fb5987ffe5e48b7040` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r12.mcfunction` | 4941 | `86ba20baac62bf383c4f00193c8001b420592834463eca8e9da89d11045b770e` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r13.mcfunction` | 4429 | `2b516bed8bc53fb24a58739d8d3a29b471f488af19f929ed1dc927edca90e1f6` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r14.mcfunction` | 3917 | `bd641cf80fb27feafb94f146935a350ea3cdd595c6ad610e5ba27c57f575090f` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r15.mcfunction` | 3405 | `a7d51d7c6ea2c27d434614dc80ba3d005da70584e133bf8aaa7e48df090e82f2` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r16.mcfunction` | 2893 | `ee4636b5375f66a7c3869652ee1f26d79db6109c0615455d08210def8704583c` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r17.mcfunction` | 2381 | `2b2db74d4c6f9e610cc7f47284a59e33824c5453e6abe0016331f917985e53d1` |
| `src/round-a/data/cushionwars_a/function/arena/gen/warn/warn_r18.mcfunction` | 1869 | `871783ea147c44d3a9068d9ab60c9d1587c8085ce9ad187e8ba9a5a0633da2ea` |
