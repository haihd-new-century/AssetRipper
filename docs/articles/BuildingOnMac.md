# Build AssetRipper trên macOS

Hướng dẫn build và đóng gói `AssetRipper.GUI.Free` từ mã nguồn trên macOS (Apple Silicon hoặc Intel) bằng `build.sh` hoặc `build.ps1` ở thư mục gốc repo. Hai script cho kết quả giống workflow CI `publish.yml`, tức là bản Native AOT cho `osx-arm64` / `osx-x64`.

## 1. Yêu cầu

| Thành phần | Bắt buộc khi | Cài đặt |
|---|---|---|
| .NET SDK 10.0+ | luôn luôn | xem mục 1.1 |
| Xcode Command Line Tools | build Native AOT (mặc định) | `xcode-select --install` |
| PowerShell 7+ | chỉ khi dùng `build.ps1` | `brew install powershell` |

### 1.1 Chọn SDK

Nên dùng SDK chính thức của Microsoft để có binary portable, chạy được trên mọi máy Mac:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
export DOTNET="$HOME/.dotnet/dotnet"     # cho build.sh / build.ps1 biết dùng SDK này
```

SDK cài qua Homebrew (`brew install dotnet`) vẫn build được, nhưng đây là bản source-built ("non-portable"). Native AOT khi đó sẽ link động tới `openssl@3` và `brotli` của Homebrew, nên binary chỉ chạy được trên Mac có cài sẵn các gói này. Script tự phát hiện trường hợp này, tự thêm đường dẫn thư viện và in cảnh báo. Bạn chỉ cần cài thêm:

```bash
brew install openssl@3 brotli
```

## 2. Build nhanh

```bash
git clone https://github.com/AssetRipper/AssetRipper.git
cd AssetRipper
./build.sh                       # Release, Native AOT, kiến trúc của máy hiện tại
```

Hoặc với PowerShell:

```powershell
./build.ps1
```

Kết quả nằm ở:

```
Source/0Bins/AssetRipper.GUI.Free/Release/<rid>/publish/AssetRipper.GUI.Free
```

Chạy thử:

```bash
Source/0Bins/AssetRipper.GUI.Free/Release/osx-arm64/publish/AssetRipper.GUI.Free
```

Trình duyệt sẽ tự mở giao diện web. Server chỉ lắng nghe trên `127.0.0.1`.

## 3. Tuỳ chọn

| `build.sh` | `build.ps1` | Tác dụng |
|---|---|---|
| `-r, --runtime <rid>` | `-Runtime <rid>` | `osx-arm64`, `osx-x64` (build chéo từ Apple Silicon được; muốn chạy bản x64 cần máy Intel hoặc Rosetta), `linux-x64`, `linux-arm64` |
| `-c, --config <cfg>` | `-Config <cfg>` | `Release` (mặc định) hoặc `Debug` |
| `-t, --test` | `-Test` | chạy toàn bộ unit test trước khi publish |
| `--clean` | `-Clean` | xoá `Source/0Bins` trước khi build |
| `--no-aot` | `-NoAot` | build self-contained JIT: nhanh (~10 giây), không cần Xcode, nhưng output lớn hơn |
| `--no-classic-linker` | `-NoClassicLinker` | dùng linker mặc định của Apple (xem mục 5) |
| `-a, --archive` | `-Archive` | tạo `AssetRipper_mac_arm64.tar.xz` giống bản phát hành |
| `--run` | `-Run` | chạy app ngay sau khi build |

Ví dụ:

```bash
./build.sh -t -a                      # chạy test, build, đóng gói
./build.sh -r osx-x64 -a              # build bản Intel trên máy Apple Silicon
./build.sh --no-aot -c Debug --run    # vòng lặp phát triển nhanh
DOTNET=$HOME/.dotnet/dotnet ./build.sh --clean -a
```

## 4. Chạy không có giao diện (headless)

```bash
./AssetRipper.GUI.Free --headless --port 8080 --log --log-path ./ripper.log
curl -X POST --data-urlencode "Path=/path/to/game.apk" http://127.0.0.1:8080/LoadFile
curl -X POST --data-urlencode "Path=/path/to/output"   http://127.0.0.1:8080/Export/UnityProject
```

Danh sách API đầy đủ xem tại `http://127.0.0.1:8080/swagger`. API không có xác thực, nên không được expose port này ra mạng.

## 5. Xử lý lỗi thường gặp

| Lỗi | Nguyên nhân / Cách xử lý |
|---|---|
| `ld: library 'ssl' not found` | Đang dùng SDK Homebrew mà chưa có `openssl@3`/`brotli`. Chạy `brew install openssl@3 brotli`, hoặc chuyển sang SDK chính thức (mục 1.1). |
| `ld: Assertion failed: ... "too many large addends"` | Lỗi của linker Apple (Xcode 16+) với object Native AOT lớn ([dotnet/runtime#119380](https://github.com/dotnet/runtime/issues/119380)). Script mặc định đã thêm `-Wl,-ld_classic`, nên đừng dùng `--no-classic-linker`. |
| `ld: warning: -ld_classic is deprecated` | Chỉ là cảnh báo, bỏ qua được. Nếu Xcode sau này xoá hẳn `ld_classic`, hãy dùng `--no-aot` cho đến khi .NET/Apple sửa lỗi. |
| `Native AOT needs Xcode Command Line Tools` | Chạy `xcode-select --install`, hoặc dùng `--no-aot`. |
| Cảnh báo `IL2026` / `IL3050` / `IL3000` khi build | Cảnh báo trim/AOT từ thư viện bên thứ ba, giống trên CI, không ảnh hưởng. |
| macOS chặn app ("cannot be opened") | Chỉ xảy ra khi copy binary sang máy khác. Chạy `xattr -dr com.apple.quarantine <thư mục>`, hoặc vào System Settings → Privacy & Security → Allow Anyway. |

## 6. Ghi chú

- Native AOT trên Apple Silicon mất khoảng 3 phút. Thư mục publish khoảng 600 MB, phần lớn là `AssetRipper.GUI.Free.dSYM` (symbol để debug), còn file `.tar.xz` khoảng 26 MB.
- Binary build bằng SDK chính thức chỉ phụ thuộc thư viện hệ thống. Kiểm tra bằng `otool -L AssetRipper.GUI.Free`.
- Muốn sinh lại gói `AssetRipper.SourceGenerated` (bước AssemblyDumper) thì dùng `Source/generate.sh`. Script đó tách biệt với việc build ứng dụng.
