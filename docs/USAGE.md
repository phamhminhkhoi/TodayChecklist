# Hướng dẫn sử dụng TodayChecklist

Tài liệu này đi từ cài đặt, thao tác hằng ngày, GitHub, dữ liệu đến xử lý sự cố. Nếu chỉ muốn bắt đầu nhanh, xem [README](../README.md).

## Mục lục

1. [Cài đặt và mở app](#1-cài-đặt-và-mở-app)
2. [Cửa sổ chính](#2-cửa-sổ-chính)
3. [Làm việc với note và task](#3-làm-việc-với-note-và-task)
4. [Ngày, lịch và chép việc chưa xong](#4-ngày-lịch-và-chép-việc-chưa-xong)
5. [GitHub issue / PR](#5-github-issue--pr)
6. [Liên kết task thành nhóm](#6-liên-kết-task-thành-nhóm)
7. [Ghi chú và tệp đính kèm](#7-ghi-chú-và-tệp-đính-kèm)
8. [Ghi chú chung](#8-ghi-chú-chung)
9. [Tìm kiếm và việc đã xong](#9-tìm-kiếm-và-việc-đã-xong)
10. [Khay hệ thống, phím tắt toàn cục, ghim](#10-khay-hệ-thống-phím-tắt-toàn-cục-ghim)
11. [Cài đặt](#11-cài-đặt)
12. [Dữ liệu, sao lưu và khôi phục](#12-dữ-liệu-sao-lưu-và-khôi-phục)
13. [Phím tắt](#13-phím-tắt)
14. [Xử lý sự cố](#14-xử-lý-sự-cố)
15. [Mẹo sử dụng](#15-mẹo-sử-dụng)
16. [Giới hạn hiện tại](#16-giới-hạn-hiện-tại)

---

## 1. Cài đặt và mở app

### 1.1. Yêu cầu

- Windows 10 hoặc Windows 11.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) nếu chạy bản publish.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) nếu build từ source.
- GitHub CLI (tuỳ chọn) nếu muốn status GitHub đầy đủ.

App chỉ chạy trên Windows. Không build hoặc chạy được trên macOS/Linux vì dùng WinForms, registry, khay hệ thống và P/Invoke `user32.dll`.

### 1.2. Build từ source

```powershell
git clone https://github.com/phamhminhkhoi/TodayChecklist.git
cd TodayChecklist
dotnet run
```

Build bản Release:

```powershell
dotnet build TodayChecklist.sln -c Release
```

### 1.3. Cài bản Release vào máy

```powershell
.\deploy.cmd
```

Script publish vào:

```text
%LOCALAPPDATA%\Programs\TodayChecklist\app-<yyyyMMdd-HHmmss>
```

rồi tạo junction:

```text
%LOCALAPPDATA%\Programs\TodayChecklist\current
```

Shortcut nên trỏ tới:

```text
%LOCALAPPDATA%\Programs\TodayChecklist\current\TodayChecklist.exe
```

Đóng app đang chạy rồi mở bản mới:

```powershell
.\deploy.cmd -Restart
```

`deploy.cmd` gọi PowerShell với `-ExecutionPolicy Bypass`, nên không cần đổi execution policy. Script giữ 3 bản gần nhất; bản đang chạy không bị xóa.

### 1.4. Lần chạy đầu

App tự tạo thư mục và DB:

```text
%LOCALAPPDATA%\TodayChecklist\checklist.db
```

Mặc định:

- Giao diện sáng, tông trung tính, màu nhấn xanh dương.
- Ngôn ngữ tiếng Việt, cỡ vừa.
- Tự chép việc chưa xong và tự cập nhật status GitHub: bật.
- Thu nhỏ xuống khay hệ thống: bật.
- Phím gọi app: `Ctrl+Alt+T`.

### 1.5. Mở một DB khác

Truyền đường dẫn DB làm tham số đầu tiên:

```powershell
TodayChecklist.exe "D:\Todo\checklist-test.db"
```

Mỗi file DB chỉ cho phép một cửa sổ. Mở lại cùng DB sẽ đưa cửa sổ đang chạy lên, không tạo bản thứ hai. Các DB khác nhau chạy song song được.

---

## 2. Cửa sổ chính

### 2.1. Header

Từ trái sang phải:

- **Tiêu đề ngày**: `Hôm nay`, `Hôm qua`, `Ngày mai`, hoặc tên thứ.
- **Ngày đầy đủ** bên dưới.
- **Nút `Ghi chú`**: mở ghi chú chung (không theo ngày).
- **Nút `Hôm nay`**: chỉ hiện khi đang xem ngày khác.
- **‹ / lịch / ›**: ngày trước, chọn ngày, ngày sau.
- **Ghim**: luôn nổi trên các cửa sổ khác.

### 2.2. Thanh tiến độ

Thanh mảnh ngay dưới header:

- Chỉ tính **việc lá** (việc không có việc con) trong DB.
- Việc đã xong đang bị ẩn vẫn được tính.
- Hiện `x/y xong`, hoặc `Xong hết` khi hoàn thành toàn bộ.

### 2.3. Ô thêm note

Dòng `+ Thêm note mới…`:

- Gõ tên rồi `Enter` để thêm một **note gốc** (cấp cao nhất).
- Dán link GitHub issue / PR vào đây: app tự rút gọn thành `#123`, phần chữ còn lại giữ nguyên, và gắn link vào việc.
- Dán link repo GitHub: tên việc thành tên repo.
- `Esc`: xóa nội dung đang gõ và quay về danh sách.
- `↓`: chuyển focus xuống danh sách.
- `Ctrl+N` từ bất kỳ đâu: quay về ô này.

### 2.4. Danh sách

Mỗi dòng có thể có:

- **Chevron** để mở / đóng việc con.
- **Ô tick** hình vuông hoặc tròn theo cài đặt.
- **Tên việc**.
- **Bộ đếm** `x/y` cho số việc con trực tiếp đã xong.
- **Chấm màu + nhãn** status GitHub.
- **Tên issue** bằng chữ xám, chỉ với các dòng chỉ ghi `#18`.
- **Icon** link, ghi chú / tệp đính kèm, coding checklist.
- **Nút hiện khi rê chuột hoặc đang chọn**: thêm việc con, mở link, sửa link, ghi chú, xóa.

Cấp bậc chỉ thể hiện bằng **thụt lề** và chevron, không có đường kẻ dọc.

### 2.5. Footer

- **Lấy việc chưa xong**: chép việc chưa xong từ ngày gần nhất sang ngày đang xem.
- **Status GitHub**: cập nhật status (giống `F5`).
- **?**: xem phím tắt (giống `F1`).
- **⚙**: cài đặt (giống `Ctrl+,`).

---

## 3. Làm việc với note và task

### 3.1. Thêm việc

| Cách | Kết quả |
|---|---|
| Gõ vào ô thêm note rồi `Enter` | Thêm note gốc |
| Chọn một dòng, `Ctrl+Enter` | Thêm việc con vào cuối |
| Chọn một dòng, `Insert` | Thêm việc con vào cuối |
| Chọn một dòng, `Enter` | Thêm việc cùng cấp ngay sau dòng đó |
| Nút `+` khi rê chuột | Thêm việc con |

Khi thêm bằng `Enter` trên một dòng đã có, app mở ô sửa tên cho dòng mới. Gõ xong `Enter` sẽ tạo và mở tiếp dòng kế; `Esc` dừng.

### 3.2. Sửa tên

- `F2`, double-click, hoặc menu chuột phải → `Sửa tên`.
- `Enter` lưu; `Esc` hủy.
- Dán link issue / PR vào tên cũng tự tách thành `#số` + link.

### 3.3. Tick xong

- Bấm ô tick hoặc nhấn `Space`.
- Tick **cha** → tick toàn bộ con, cháu.
- Tất cả con xong → cha tự xong.
- Bỏ tick một con → cha tự bỏ xong.
- Mỗi việc xong được ghi `done_at` (ngày giờ máy). Bỏ tick sẽ xóa giờ.

### 3.4. Xóa

- `Delete` hoặc menu chuột phải → `Xóa`.
- Nếu việc có việc con, app hỏi xác nhận và xóa cả cây.
- Nếu là việc lá, app xóa ngay. **Chưa có undo**, hãy cẩn thận.

### 3.5. Ẩn việc đã xong

- `Ctrl+Shift+H` hoặc nút hình con mắt ở footer.
- Việc vừa tick được giữ lại khoảng 1,2 giây để kịp bỏ tick, sau đó mới ẩn.
- Việc đã xong đang ẩn vẫn tính vào thanh tiến độ.
- Trạng thái ẩn / hiện được lưu vào DB.

### 3.6. Đổi thứ tự và cấp bậc

- **Kéo thả** dòng:
  - Thả giữa hai dòng: đổi thứ tự.
  - Thả vào giữa một dòng: thành việc con của dòng đó.
  - `Esc` khi đang kéo: hủy.
- `Alt+↑` / `Alt+↓`: đổi thứ tự trong cùng cấp.
- `Alt+→`: thụt vào, thành con của dòng ngay trên.
- `Alt+←`: lùi ra, thành việc cùng cấp với cha.
- Trạng thái mở / đóng của từng dòng được lưu lại.

### 3.7. Menu chuột phải

- Mở link / mở link cả nhóm.
- Gắn link / sửa link / bỏ link.
- Ghi chú & tệp đính kèm.
- Liên kết với task khác / bỏ liên kết task.
- Sửa tên, thêm việc con, xóa.

---

## 4. Ngày, lịch và chép việc chưa xong

### 4.1. Đổi ngày

- `Ctrl+PgUp` / `Ctrl+PgDn`: ngày trước / ngày sau.
- `Ctrl+Home`: về hôm nay (khi không đang gõ chữ).
- Nút `‹` / `›` cạnh lịch.
- Bấm icon lịch để chọn ngày.

### 4.2. Lịch

- Hiện tháng đang xem; dùng `‹` / `›` hoặc lăn chuột để đổi tháng.
- **Chấm màu nhấn** dưới số ngày: hôm nay.
- **Chấm xám** dưới số ngày: ngày còn việc chưa xong và chưa được chép sang ngày sau.
- Bấm một ngày để mở ngày đó.

### 4.3. Tự chép việc chưa xong

Mặc định bật. Khi mở app hoặc khi qua ngày mới trong lúc app đang chạy:

- App tìm **ngày gần nhất trước hôm nay có ghi việc**.
- Nếu ngày đó còn việc chưa xong, app chép sang hôm nay.
- Chỉ tự chép **một lần mỗi ngày**. Xóa việc vừa chép rồi mở lại app sẽ không chép lại.
- Nếu ngày gần nhất đã xong hết, app không lục các ngày cũ hơn.

Có thể tắt trong **Cài đặt → Tự động → Việc chưa xong → Bấm tay**.

### 4.4. Chép tay

Bấm **Lấy việc chưa xong** ở footer:

- App tìm ngày gần nhất trước ngày đang xem còn việc chưa xong.
- Hỏi xác nhận rồi chép sang ngày đang xem.
- Nếu ngày đích đã có việc cùng tên ở cùng chỗ, app gộp, không nhân đôi.
- Nếu không còn việc chưa xong, app báo rõ.

### 4.5. Việc được chép gồm những gì?

- Toàn bộ cây việc chưa xong.
- Link, status GitHub, tên issue, coding checklist.
- Ghi chú.
- Tệp / thư mục đính kèm.
- Nhóm task và màu nhóm.

Việc nguồn được đánh dấu `carried`; nó không còn được tính là "việc sót" trên lịch.

---

## 5. GitHub issue / PR

App **chỉ đọc** GitHub: không comment, không đóng issue, không sửa project, không tạo gì trên GitHub.

### 5.1. Cài token

Không có token, app vẫn đọc issue / PR của repo public bằng REST (open / draft / merged / closed / not planned), nhưng không có Project status và coding checklist.

Cách khuyến nghị:

```powershell
gh auth login
gh auth refresh -s read:project
```

- `repo`: cần cho repo private.
- `read:project`: cần để đọc cột Status của GitHub Project.

Hoặc đặt biến môi trường:

```powershell
$env:GH_TOKEN = "ghp_..."
```

App đọc theo thứ tự:

1. `GH_TOKEN`
2. `GITHUB_TOKEN`
3. `gh auth token`

Token chỉ giữ trong bộ nhớ, không lưu vào DB. Đổi token thì khởi động lại app.

### 5.2. Gắn link vào việc

Có 4 cách:

1. **Dán link issue / PR vào ô thêm note**:
   ```text
   https://github.com/owner/repo/issues/123
   ```
   → tên việc thành `#123`, link được gắn vào việc.

2. **Dán link kèm chữ**:
   ```text
   https://github.com/owner/repo/pull/45 sửa grid
   ```
   → tên việc thành `#45 sửa grid`.

3. **`Ctrl+L`** trên một việc đã có, hoặc menu chuột phải → `Gắn link…`. Nếu clipboard đang có URL, app gợi ý sẵn.

4. **Link repo ở note cha**:
   - Gắn link repo cho note cha:
     ```text
     https://github.com/owner/repo
     ```
   - Các dòng con chỉ ghi `#18`, `#58`… sẽ tự hiểu là issue của repo đó.
   - Link suy ra này **không lưu riêng** trên việc con; đổi link note cha là đổi theo.

Link phải bắt đầu bằng `http://` hoặc `https://`.

### 5.3. Status hiển thị

| Nguồn | Nhãn | Chấm |
|---|---|---|
| Cột `Status` của GitHub Project | Tên option (Todo, In progress, Done…) | Màu của option: xám, xanh lá, tím, đỏ, vàng, cam, xanh dương, hồng |
| Issue / PR đang mở | `Open` | Xanh lá |
| PR draft | `Draft` | Xám |
| PR đã merge | `Merged` | Tím |
| PR đóng không merge | `Closed` | Đỏ |
| Issue đã đóng | `Closed` | Tím |
| Issue closed as not planned / duplicate | `Not planned` | Xám |

Quy tắc:

- Nếu issue / PR nằm trong nhiều project, app lấy project đầu tiên có cột `Status`.
- Status chỉ hiện khi link hiện tại **vẫn là link lúc lấy status**. Đổi link thì status cũ tự ẩn.
- Với dòng chỉ ghi `#18`, app hiện thêm tên issue lấy từ GitHub ngay sau tên việc.
- Chấm màu giữ nghĩa quen thuộc của GitHub, không tô nền.

### 5.4. Coding checklist

Icon **kẹp giấy** trước status nghĩa là issue / PR có coding checklist. Bấm icon để mở.

App nhận diện checklist khi:

- Có heading `Coding Checklist` (heading Markdown hoặc chữ đậm), hoặc
- Nội dung có đủ `Impacted Modules` và `Logic Flow`.

App lấy checklist **mới nhất** trong:

1. Các comment (tối đa 100 comment gần nhất), sau đó
2. Phần mô tả issue / PR.

Coding checklist chỉ lấy được khi có token GraphQL. Không có token thì icon không hiện.

### 5.5. Cập nhật status

- `F5` hoặc nút **Status GitHub**: cập nhật các task GitHub **trong ngày đang xem**.
- App tự cập nhật khi mở app và mỗi 30 phút (nếu bật).
- Task vừa thêm hoặc vừa gắn link được cập nhật ngay, không đợi 30 phút.
- Việc đã xong đang bị ẩn không được cập nhật.
- Đang cập nhật thì nút hiện `Đang cập nhật…`; xong hiện số task cập nhật được.
- Lỗi một vài task: app báo danh sách lỗi; lỗi khi tự động thì giữ icon cảnh báo trên nút.

### 5.6. Giới hạn request

Không có token, GitHub REST chỉ cho khoảng **60 request/giờ**. Mỗi task GitHub là một request, và app tự cập nhật mỗi 30 phút, nên chỉ cần hơn 30 task là có thể bị `403/429`.

Cách xử lý:

- Đăng nhập `gh auth login` để dùng GraphQL (giới hạn cao hơn nhiều).
- Hoặc tắt tự cập nhật: **Cài đặt → Tự động → Status GitHub → Bấm tay (F5)**.
- Đợi hết giờ reset rồi bấm `F5` lại.

---

## 6. Liên kết task thành nhóm

Dùng khi nhiều task cùng thuộc một việc lớn, ví dụ 2 issue ở 2 repo.

### 6.1. Tạo nhóm

1. Chọn một task.
2. `Ctrl+G` hoặc menu chuột phải → `Liên kết với task khác…`.
3. Tick các task cùng nhóm (chỉ các task **trong cùng ngày**).
4. Chọn màu.
5. Bấm đồng ý.

Các task cùng nhóm được tô cùng màu, có cùng ID nhóm trong DB local.

### 6.2. Mở link cả nhóm

- `Shift + bấm tên việc` có link, hoặc
- `Ctrl+Shift+O`.

App mở link của mọi task cùng nhóm, bỏ trùng. Nếu hơn 10 link, app hỏi xác nhận trước.

### 6.3. Bỏ liên kết

- Menu chuột phải → `Bỏ liên kết task` để đưa một task ra khỏi nhóm.
- Nếu nhóm chỉ còn 1 task, app tự bỏ nhóm (một mình một màu không có nghĩa liên kết).

Nhóm chỉ lưu trên máy, không tạo gì trên GitHub.

---

## 7. Ghi chú và tệp đính kèm

`Ctrl+E` hoặc menu chuột phải → `Ghi chú & tệp đính kèm…`.

### 7.1. Ghi chú

- Ô nhiều dòng, lưu khi đóng hộp (`Xong`, `Esc`, hoặc nút X).
- Nội dung ghi chú được tính vào tìm kiếm.
- Icon ghi chú hiện trên dòng task khi có nội dung.

### 7.2. Tệp và thư mục

- **Thêm tệp…**: chọn một hoặc nhiều tệp.
- **Thêm thư mục…**: chọn một thư mục.
- **Kéo thả** tệp / thư mục từ Explorer vào hộp thoại.
- Mỗi dòng có nút **Mở** và nút bỏ đính kèm.
- App chỉ lưu **đường dẫn tuyệt đối**, không copy file vào DB.
- Nếu file gốc bị di chuyển / xóa, app hiện cảnh báo "không còn trên máy".

---

## 8. Ghi chú chung

Dành cho thông tin không đổi theo ngày: link hay dùng, tài khoản test, lệnh, ghi chú dài.

- `Ctrl+J` hoặc nút **Ghi chú** trên header.
- Cửa sổ riêng, không chặn cửa sổ việc.
- Đóng cửa sổ là tự lưu.
- `Ctrl+Win+Alt` từ bất kỳ đâu: mở ghi chú chung.
  - Đang mở trước mặt thì bấm lại để đóng và lưu.
  - Không cần mở cửa sổ việc.
- Tắt phím này trong **Cài đặt → Chung → Phím mở ghi chú**.

Ghi chú chung lưu trong bảng `settings` của DB, không theo ngày.

---

## 9. Tìm kiếm và việc đã xong

### 9.1. Tìm kiếm

`Ctrl+F`:

- Tìm ở **mọi ngày**.
- Phạm vi: tên việc, tên issue GitHub, ghi chú.
- Không phân biệt hoa thường.
- **Không cần gõ dấu**: `cap nhat` khớp `cập nhật`.
- Tất cả các từ gõ vào đều phải xuất hiện.
- Mới nhất trước, tối đa 300 kết quả.
- Kết quả hiện ngày, tên việc, tên issue / dòng đầu ghi chú, note cha.
- Việc đã xong hoặc đã được chép sang ngày sau được làm mờ.
- `Enter` mở kết quả đầu tiên; bấm một kết quả để mở ngày đó và chọn việc.

### 9.2. Việc đã xong

`Ctrl+H`:

- Hiện 500 việc đã xong gần nhất, mới nhất ở trên.
- Nhóm theo ngày xong, kèm giờ xong.
- Việc xong từ trước khi app ghi giờ thì xếp theo ngày của việc.
- Bấm một việc để mở ngày đó; nếu việc đang bị ẩn, app tự bỏ ẩn để thấy.

---

## 10. Khay hệ thống, phím tắt toàn cục, ghim

### 10.1. Khay hệ thống

Mặc định bật **thu nhỏ xuống khay**:

- Bấm minimize → ẩn cửa sổ, còn icon ở khay.
- Bấm trái icon khay → hiện cửa sổ.
- Chuột phải icon khay → `Mở`, `Thêm việc mới`, `Thoát`.

Lưu ý: bấm **X** là **thoát app**, không phải thu nhỏ xuống khay. Muốn thu nhỏ, dùng nút minimize hoặc phím gọi app.

Tắt khay trong **Cài đặt → Chung → Thu nhỏ → Như thường**.

### 10.2. Phím gọi app

Mặc định `Ctrl+Alt+T`. Có thể đổi thành `Win+Shift+T` hoặc tắt.

- App đang mở trước mặt: bấm phím để cất đi (khay hoặc minimize).
- App đang ẩn / thu nhỏ: bấm phím để mở hôm nay và đặt con trỏ vào ô thêm việc.
- Nếu phím bị Windows hoặc app khác giữ, app báo ngay khi đổi cài đặt.

### 10.3. Ghim

Nút ghim trên header bật / tắt `TopMost`. Trạng thái ghim không được lưu qua lần mở sau.

---

## 11. Cài đặt

`Ctrl+,` hoặc nút ⚙. Thay đổi áp dụng ngay, không cần khởi động lại.

| Nhóm | Cài đặt | Lựa chọn | Mặc định |
|---|---|---|---|
| Màu sắc | Giao diện | Sáng / Tối / Theo Windows | Sáng |
| | Tông nền | Trung tính / Lạnh / Ấm | Trung tính |
| | Màu nhấn | 8 màu có sẵn + tự chọn | Xanh dương |
| Nút | Hình dạng | Vuông / Bo góc / Tròn | Bo góc |
| | Nút chính | Màu mực / Màu nhấn / Viền | Màu mực |
| Tự động | Việc chưa xong | Tự chép / Bấm tay | Tự chép |
| | Status GitHub | Tự cập nhật / Bấm tay | Tự cập nhật |
| Chung | Ngôn ngữ | Tiếng Việt / English | Tiếng Việt |
| | Kích thước | Nhỏ / Vừa / Lớn / Rất lớn | Vừa |
| | Thu nhỏ | Xuống khay / Như thường | Xuống khay |
| | Phím gọi app | Ctrl+Alt+T / Win+Shift+T / Tắt | Ctrl+Alt+T |
| | Phím mở ghi chú | Ctrl+Win+Alt / Tắt | Bật |

- **Theo Windows**: đổi sáng / tối theo Windows Settings; app cập nhật khi Windows đổi.
- **Màu nhấn tự chọn**: bản tối tự pha thêm trắng để đọc được.
- **Kích thước**: đổi cỡ chữ, khoảng cách và kích thước cửa sổ theo tỉ lệ.

---

## 12. Dữ liệu, sao lưu và khôi phục

### 12.1. Vị trí DB

```text
%LOCALAPPDATA%\TodayChecklist\checklist.db
```

DB chứa các bảng:

- `items`: việc dạng cây, ngày, trạng thái xong, link, status GitHub, ghi chú…
- `attachments`: đường dẫn tệp / thư mục đính kèm.
- `task_groups`: nhóm task và màu.
- `settings`: giao diện, ngôn ngữ, ghi chú chung, cờ tự động…

### 12.2. Sao lưu

App chưa có backup tự động. Cách an toàn:

1. Đóng app.
2. Copy file:

```powershell
Copy-Item "$env:LOCALAPPDATA\TodayChecklist\checklist.db" "D:\Backup\checklist-$(Get-Date -Format yyyyMMdd).db"
```

Nên sao lưu trước khi xóa nhiều việc hoặc thử nghiệm.

### 12.3. Khôi phục

1. Đóng app.
2. Chép file backup đè lên `checklist.db` (hoặc mở trực tiếp file backup bằng tham số DB).
3. Mở app.

### 12.4. Gỡ cài đặt

- Xóa thư mục `%LOCALAPPDATA%\Programs\TodayChecklist`.
- DB vẫn còn ở `%LOCALAPPDATA%\TodayChecklist`; xóa riêng nếu muốn xóa sạch dữ liệu.

### 12.5. Di chuyển sang máy khác

Copy `checklist.db`. Tệp đính kèm vẫn trỏ đường dẫn cũ; cần copy file / thư mục đó sang cùng đường dẫn hoặc gắn lại.

---

## 13. Phím tắt

### Chung

| Phím | Việc |
|---|---|
| `Ctrl+N` | Tới ô thêm note mới |
| `Ctrl+,` | Mở cài đặt |
| `F1` | Xem phím tắt |
| `Ctrl+F` | Tìm ở mọi ngày |
| `Ctrl+H` | Việc đã xong |
| `Ctrl+Shift+H` | Ẩn / hiện việc đã xong |
| `Ctrl+J` | Ghi chú chung |
| `Ctrl+Win+Alt` | Mở / đóng ghi chú chung từ bất kỳ đâu |
| `Ctrl+Alt+T` | Gọi app lên / cất đi (đổi được) |
| `F5` | Cập nhật status GitHub |
| `Ctrl+PgUp` / `Ctrl+PgDn` | Ngày trước / ngày sau |
| `Ctrl+Home` | Về hôm nay (khi không đang gõ chữ) |

### Trong danh sách

| Phím | Việc |
|---|---|
| `Enter` | Thêm việc cùng cấp ngay sau dòng đang chọn |
| `Ctrl+Enter` / `Insert` | Thêm việc con |
| `F2` / double-click | Sửa tên |
| `Space` | Tick xong / bỏ tick |
| `Delete` | Xóa |
| `Alt+↑` / `Alt+↓` | Đổi thứ tự |
| `Alt+←` / `Alt+→` | Lùi / thụt cấp |
| `Ctrl+L` | Gắn / sửa link |
| `Ctrl+O` | Mở link |
| `Ctrl+Shift+O` | Mở link cả nhóm |
| `Ctrl+G` | Liên kết task |
| `Ctrl+E` | Ghi chú & tệp đính kèm |
| `Esc` | Hủy sửa tên / hủy kéo thả |

### Trong ô thêm note

| Phím | Việc |
|---|---|
| `Enter` | Thêm việc |
| `Esc` | Xóa nội dung, về danh sách |
| `↓` | Xuống danh sách |

### Trong ô sửa tên

| Phím | Việc |
|---|---|
| `Enter` | Lưu; với việc mới thì mở tiếp dòng kế |
| `Esc` | Hủy |

---

## 14. Xử lý sự cố

### App không mở

- Kiểm tra đã cài **.NET 10 Desktop Runtime** chưa:
  ```powershell
  dotnet --list-runtimes
  ```
- App chỉ chạy trên Windows.
- Nếu Windows SmartScreen cảnh báo: chọn **More info → Run anyway** (bản build cá nhân chưa ký số).

### `dotnet` không nhận lệnh

- Cài .NET 10 SDK.
- Mở lại terminal / PowerShell sau khi cài.
- Kiểm tra:
  ```powershell
  dotnet --version
  ```

### Không lấy được status GitHub

- Link phải là issue hoặc pull request, không phải link repo:
  ```text
  https://github.com/owner/repo/issues/123
  https://github.com/owner/repo/pull/45
  ```
- Repo private cần token có quyền `repo`.
- Bấm `F5` lại; xem thông báo lỗi cụ thể.
- Nếu link vừa đổi, status cũ bị ẩn; `F5` để lấy lại.

### `404` / `not found`

- Sai số issue / PR.
- Repo private nhưng token không có quyền.
- Repo / issue đã bị xóa hoặc chuyển.

### `403` / `429` / rate limited

- Đang dùng REST không token (60 request/giờ).
- Đăng nhập `gh auth login` để dùng GraphQL.
- Hoặc tắt tự cập nhật, đợi hết giờ rồi `F5`.

### Không thấy cột Status của GitHub Project

- Token thiếu scope `read:project`:
  ```powershell
  gh auth refresh -s read:project
  ```
- Issue / PR không nằm trong project, hoặc project đã archive.
- Cột không tên `Status` hoặc là loại field khác (app chỉ đọc single-select tên `Status`).

### Không thấy icon coding checklist

- Cần token GraphQL.
- Nội dung phải có heading `Coding Checklist`, hoặc đủ `Impacted Modules` + `Logic Flow`.
- App chỉ đọc 100 comment gần nhất và phần mô tả.

### Phím tắt toàn cục không hoạt động

- Phím đang bị Windows / app khác giữ. Đổi sang `Win+Shift+T` trong Cài đặt.
- Một số phần mềm bảo mật chặn hook bàn phím; thử tắt / mở lại app.

### `Ctrl+Win+Alt` không mở ghi chú

- Tắt rồi bật lại trong **Cài đặt → Chung → Phím mở ghi chú**.
- Một số bàn phím / driver không gửi tổ hợp này đúng; dùng `Ctrl+J`.

### Chữ / icon hiển thị lạ

- Windows 11 có đủ font; Windows 10 dùng font dự phòng nên hơi khác.
- Kiểm tra **Cài đặt → Kích thước**.

### Mở nhầm DB

- Shortcut có thể đang truyền tham số DB khác.
- Xem đường dẫn DB ở cuối hộp `F1`.
- Đóng app rồi mở lại không kèm tham số để dùng DB mặc định.

### Deploy báo file đang được dùng

- App đang chạy. Dùng:
  ```powershell
  .\deploy.cmd -Restart
  ```
- Hoặc đóng app (kể cả icon khay) rồi deploy lại.
- Antivirus có thể giữ file; thử lại sau vài giây.

### Dữ liệu biến mất

- Kiểm tra đang mở đúng DB (cuối hộp `F1`).
- Khôi phục từ file backup nếu có.
- App không có undo xóa; tránh xóa nhầm.

---

## 15. Mẹo sử dụng

- **Dùng note cha làm dự án**: note cha là tên repo / dự án, các dòng con là `#18`, `#58`… Gắn link repo cho note cha một lần, các con tự thành link issue.
- **Dùng cấp bậc thay vì nhiều app**: note lớn → việc con → bước nhỏ. Tick cha / con tự đồng bộ.
- **F5 sau khi đổi trạng thái trên GitHub**: status trong app là ảnh chụp lúc cập nhật, không realtime.
- **Nhóm task cho việc nhiều repo**: `Ctrl+G`, chọn các issue liên quan, chọn màu; sau đó `Ctrl+Shift+O` mở cả bộ link.
- **Ẩn việc xong khi tập trung**: `Ctrl+Shift+H`; việc xong vẫn tính vào tiến độ.
- **Tìm không dấu**: gõ nhanh `review ui`, `cap nhat`, `deploy`… không cần gõ tiếng Việt có dấu.
- **Ghi chú chung cho lệnh hay dùng**: `Ctrl+J`, dán lệnh / link / tài khoản test; mở từ bất kỳ đâu bằng `Ctrl+Win+Alt`.
- **Sao lưu trước khi dọn dẹp**: app chưa có undo.

---

## 16. Giới hạn hiện tại

- Chưa có backup tự động.
- Chưa có undo khi xóa.
- Chưa có nhắc việc / thông báo đến hạn.
- Chưa có việc lặp lại theo ngày / tuần / tháng.
- Chưa có chức năng chuyển một task sang ngày khác (chỉ có chép việc chưa xong).
- Liên kết task trong `Ctrl+G` chỉ chọn được task trong cùng ngày.
- Tệp đính kèm chỉ lưu đường dẫn, không copy file.
- Status GitHub chỉ cập nhật khi mở app / mỗi 30 phút / bấm `F5`, không realtime.
- Không có đồng bộ đám mây hay nhiều người dùng.

Nếu cần một trong các mục trên, có thể mở issue trên GitHub để thảo luận.
