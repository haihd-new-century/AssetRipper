# Kế hoạch hiện thực các tính năng Premium

> **Trạng thái: HOÀN THÀNH.** Cả 7 tính năng đều đã có, edition đã đổi tên thành **AssetRipper Freemium**. Kết quả kiểm chứng xem [mục 5](#5-kết-quả-thực-hiện).

Nguồn yêu cầu: `docs/Premium Edition.txt` (7 tính năng). Sau khi hoàn thành, đổi tên phiên bản từ **Free** thành **Freemium**.

## 1. Hiện trạng (trước kế hoạch)

| # | Tính năng | Trạng thái | Ghi chú |
|---|---|---|---|
| 1 | Shader Decompilation | Chưa có | `ShaderExportMode.Decompile` tồn tại nhưng không có exporter; mã Free không đọc được blob shader |
| 2 | Static Mesh Separation | **Đã có** | `StaticMeshSeparationProcessor` (commit `b792bb67c`) |
| 3 | Prefab Outlining | Chưa có | Setting `EnablePrefabOutlining` có, nhưng không có processor |
| 4 | Il2Cpp Analysis | Một phần | Có khôi phục giá trị khởi tạo (#8) và hook `RecoveryProcessingLayers`/`RecoveryOutputFormat` (đang `null`) |
| 5 | Asset Path Overrides | **Đã có** | `AssetPathOverrideProcessor` (commit `9665a76d9`) |
| 6 | User-Defined Package Export | Chưa có | |
| 7 | Asset Deduplication | **Đã có** | `AssetDeduplication` (commit `c6beb7724`) |

## 2. Phân tích kỹ thuật và thiết kế

### 2.1 Il2Cpp Analysis (Script Content Level 3)
- Cpp2IL (`AssetRipper.Cpp2IL.Core` 1.0.9) có sẵn:
  - Output format `AsmResolverDllOutputFormatIlRecovery` (ISIL → IL).
  - Các layer `CallAnalysisProcessingLayer`, `NativeMethodDetectionProcessingLayer`, `StableRenamingProcessingLayer`, `DeobfuscationMapProcessingLayer`.
- Thiết kế:
  - Đăng ký mặc định `RecoveryOutputFormat = IlRecovery` và `RecoveryProcessingLayers = [AttributeAnalysis, MethodOverrideNameFixer, NativeMethodDetection, CallAnalysis]` khi `ScriptContentLevel.Level3`.
  - Bỏ qua mscorlib, `System*`, `Unity*` để giữ tốc độ (giống bản Premium).
  - Method nào phục hồi thất bại thì giữ nguyên dạng stub hiện có (`throw null`), không làm vỡ quá trình compile.
  - Thống kê số method phục hồi thành công / thất bại, ghi vào log.
- Kiểm chứng: APK test với Level3; so sánh thân `TestBehaviour.Update()`, và đảm bảo project vẫn xuất được.

### 2.2 User-Defined Package Export
- Vấn đề: project xuất ra chứa script/DLL của các package (TextMeshPro, Input System…). Khi người dùng thêm package thật, script bị trùng và tham chiếu bị gãy.
- Thiết kế:
  - Singleton cấu hình `PackageMappings` (JSON), chỉnh qua Configuration Files hoặc tham số CLI `--packages`.
  - Cấu trúc: `{ "Packages": [ { "Name": "com.unity.textmeshpro", "Version": "3.0.6", "Assemblies": ["Unity.TextMeshPro"], "Scripts": { "TMPro.TextMeshProUGUI": "<guid>" }, "Assets": { "<collection>/<pathId>": "<guid>:<fileId>" } } ] }`.
  - Khi export:
    - MonoScript thuộc assembly của package không được xuất; tham chiếu tới nó trỏ thẳng vào GUID của package (fileID `11500000`).
    - DLL của các assembly đó không được ghi vào `Assets/Plugins`.
    - Asset được ánh xạ dùng GUID/fileID của package.
    - Package được thêm vào `Packages/manifest.json`.
  - Hạ tầng sẵn có: `RedirectExportCollection` (MetaPtr theo GUID), `PackageManifestPostExporter`.
- Kiểm chứng: test export với một mapping giả, kiểm tra GUID trong YAML, manifest và việc DLL không được ghi.

### 2.3 Shader Decompilation
- Dữ liệu:
  - `IShader.CompressedBlob` và `Offsets/CompressedLengths/DecompressedLengths` theo từng platform. Mỗi đoạn được nén LZ4 và chứa các sub-program (header + bytecode).
  - `ParsedForm` (SerializedShader) mô tả Properties, SubShader, Pass, state, keyword và `BlobIndex` của từng sub-program.
- Phạm vi giai đoạn 1 (khả thi, có kiểm chứng):
  1. Giải nén và tách blob thành sub-program (`ShaderBlobReader`).
  2. Với GpuProgramType là GLES/GLES3/GLCore, bytecode chính là mã nguồn GLSL. Phần này được xuất thành ShaderLab hoàn chỉnh: `Properties`, `SubShader/Tags/LOD`, `Pass` với render state (Cull/ZWrite/ZTest/Blend/ColorMask/Stencil), và `GLSLPROGRAM … ENDGLSL` cho từng variant.
  3. Các platform khác (DX11 DXBC, Metal, Vulkan SPIR-V) chưa có trình dịch ngược. Khi gặp, xuất phần ShaderLab giống trên kèm comment liệt kê các sub-program (không có code), và fallback sang shader dummy để vẫn compile được.
- Kích hoạt: `ShaderExportMode.Decompile`, có trong Settings.
- Kiểm chứng: APK Android (GLES3) → file `.shader` có `GLSLPROGRAM`, đúng số pass/property; có test đơn vị cho bộ đọc blob.

### 2.4 Prefab Outlining
- Vấn đề: khi build, các prefab bị “inline” vào scene.
- Thiết kế giai đoạn 1:
  1. Với mỗi GameObject gốc (root hoặc con trực tiếp) trong các scene, tính chữ ký cấu trúc gồm tên đã bỏ hậu tố `(1)`/`(Clone)`, danh sách component theo kiểu, và đệ quy cho các con.
  2. Nhóm theo chữ ký; nhóm nào có ≥ 2 instance (hoặc trùng tên một prefab sẵn có) là ứng viên.
  3. Tạo prefab asset (`PrefabHierarchyObject`, đặt trong collection mới “Outlined Prefabs”) bằng cách clone hierarchy của instance đầu tiên. Đặt `OverridePath = Assets/Prefabs/Outlined/<Name>.prefab`.
  4. Giai đoạn 1 không thay instance trong scene bằng PrefabInstance (việc đó cần modification và stripped object, sẽ làm ở giai đoạn 2). Scene vẫn đúng, người dùng có thêm prefab để tái sử dụng.
- Kích hoạt: `EnablePrefabOutlining` (đã có trong Settings).
- Kiểm chứng: build một scene có 3 bản sao của cùng một hierarchy → xuất ra 1 prefab; có test chữ ký cấu trúc.

### 2.5 Đổi tên Free → Freemium
- Chuỗi hiển thị `asset_ripper_free` trong `Localizations/*.json` (en_US = "AssetRipper Freemium").
- Tiêu đề / thanh điều hướng / tài liệu HTML / `docs/articles/PremiumFeatures.md`.
- Không đổi tên assembly/project (`AssetRipper.GUI.Free`) để tránh phá vỡ build, CI và đường dẫn phát hành.

## 3. Thứ tự thực hiện

| Bước | Nội dung | Tiêu chí hoàn thành |
|---|---|---|
| 1 | Il2Cpp Analysis (Level3) | Build pass, log thống kê, test e2e Level3 |
| 2 | User-Defined Package Export | Test GUID/manifest/DLL |
| 3 | Shader Decompilation giai đoạn 1 | Test bộ đọc blob + e2e `.shader` có GLSLPROGRAM |
| 4 | Prefab Outlining giai đoạn 1 | Test chữ ký + e2e prefab |
| 5 | Mở khoá các tuỳ chọn trong GUI, cập nhật trang Premium Features | Các checkbox/dropdown không còn disabled |
| 6 | Đổi Free → Freemium | `grep` không còn "AssetRipper Free" trong UI |
| 7 | Cập nhật tài liệu HTML, chạy toàn bộ test và AOT build | 0 lỗi |

## 4. Rủi ro

- ISIL → IL của Cpp2IL còn thử nghiệm; tỉ lệ phục hồi thấp (Premium công bố 10–20% trên x86, ARM còn thấp hơn). Method nào phục hồi lỗi phải rơi về stub.
- Shader: chỉ GLSL được dịch ngược thật; DX/Metal/SPIR-V cần trình dịch ngược riêng (ngoài phạm vi).
- Prefab Outlining giai đoạn 1 không liên kết instance trong scene với prefab.

## 5. Kết quả thực hiện

| # | Tính năng | Commit | Bằng chứng |
|---|---|---|---|
| 1 | Shader Decompilation (GLSL) | `bc9635752` | Shader tuỳ chỉnh từ bản build GLES3 6000.3.26 → `.shader` có `GLSLPROGRAM`; Unity import lại không lỗi (1 pass, 3 property); 8 unit test |
| 2 | Static Mesh Separation | `b792bb67c` | 9 renderer → 3 mesh, bounds local đúng |
| 3 | Prefab Outlining | `fa6ee1c1e` | `static.apk` → 3 prefab; Unity load được kèm mesh và material, 0 lỗi compile; 8 unit test |
| 4 | Il2Cpp Analysis (Level 3) | `e4a15dedb` | Log: *1 of 5 game methods recovered (20.0%)* trên ARM64, các method lỗi giữ stub compile được; 9 unit test |
| 5 | Asset Path Overrides | `9665a76d9` | Test export ra `Assets/Models/Renamed.asset` |
| 6 | User-Defined Package Export | `36e8dfcf8` | DLL và script của package không bị xuất; scene trỏ tới GUID script của package; manifest có package |
| 7 | Asset Deduplication | `c6beb7724` | 2 bundle cùng mesh → 1 file |
| – | Đổi Free → Freemium | `4730e0469` | Tiêu đề và brand hiển thị "AssetRipper Freemium"; Settings không còn mục nào bị khoá |
| – | Explorer 2 khung cho GameBundle | `9a0e76830` | Chrome thật: 60 nút cây, 58 ô, 630 asset trong collection, xem trước 384×384, 0 lỗi JS |

Toàn bộ test: **642/642** đạt.
