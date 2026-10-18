# MacWindowSnap

Ung dung chup cua so (Window Capture) mang phong cach macOS danh cho Windows, tich hop san tinh nang tu dong padding trong suot va boc tach bong do tu nhien (DWM Drop Shadow) cua Windows.

## Tinh nang chinh
- Phim tat toan cuc: Binh thuong an o khay he thong (System Tray), bam `Ctrl + Shift + A` o bat ky dau de kich hoat.
- Re chuot tu lua cua so (Hover Detection): Di chuyen chuot den dau se tu dong highlight vien xanh macOS va hien badge thong tin ung dung + kich thuoc pixel (W x H) den do.
- Keo chuot chon dialog bat ky (Drag to Select): Neu gap cac dialog "ve chay" ben trong web/app (modal HTML, popup Figma...), chi can keo chuot bao quanh dialog do. App se tu dong bo tron goc (10px) va phu dai bong do mem 2 lop cua macOS cuc dep.
- Chup co bong do goc (Authentic DWM Shadow): Khi click chuot vao cua so/dialog Windows, app su dung thuat toan boc tach alpha da lop cua DWM de giu nguyen ven dai bong mo tu nhien cua Windows (ho tro goc bo Windows 11).
- Tu dong copy vao Clipboard: Luu dinh dang PNG 32-bit alpha va DIBV5 vao Clipboard de paste ngay vao Telegram, Zalo, Slack, Discord, Photoshop, Word... ma khong bi nen den.
- Tu dong luu file: Mac dinh tu dong luu file PNG vao thu muc `Pictures/Screenshots/`.
- Am thanh chup anh: Phat am thanh shutter nhe nhang bao hieu da chup xong.

## Phim tat trong che do chup
- Click chuot trai hoac Space / Enter: Chup cua so dang duoc highlight kem bong do goc.
- Keo chuot trai: Ve khung chon bat ky dialog hoac vung nao de tu dong tao bong do macOS.
- Chuot phai hoac Esc: Huy bo che do chup.

## Khoi dong cung Windows
De ung dung tu khoi dong khi bat may:
1. Nhan to hop phim `Win + R`, go `shell:startup` roi Enter.
2. Tao shortcut cua file `MacWindowSnap.exe` (tai thu muc `publish`) bo vao thu muc Startup vua mo.
