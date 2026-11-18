# EV Charge & Swap Management System

Nền tảng quản lý địa điểm sạc ô tô điện và đổi pin xe máy điện, phục vụ tìm địa điểm trên bản đồ, giữ chỗ, ghi nhận sử dụng dịch vụ, tính phí và thanh toán.

Backend dùng **ASP.NET Core .NET 9 + EF Core + PostgreSQL**, tổ chức thành một ứng dụng monolith chia thư mục theo chức năng. Frontend là **Angular SPA**, dùng Leaflet và OpenStreetMap cho bản đồ.

Tài liệu nguồn của dự án:

- [Product requirements](docs/requirements.md)
- [Architecture and technology roadmap](docs/architecture-roadmap.md)

## 1. Trạng thái triển khai

| Hạng mục | Hiện trạng |
| --- | --- |
| Locations | Đã có entity, mapping EF Core, service, response DTO, API đọc/phân trang và trạng thái vận hành do Admin cập nhật |
| PostgreSQL | Đã có cấu hình provider, migration đầu tiên, Docker Compose và seed local tùy chọn |
| Redis | Container Redis 7 Alpine, cache thông tin trạm với explicit invalidation và fallback tự động |
| API trong Docker | Có Dockerfile multi-stage, runtime non-root và job migration riêng cho local |
| Swagger UI | Có tại `/swagger`, chỉ trong Development |
| Common | Đã có phân trang dùng chung và xử lý lỗi không lộ thông tin nội bộ |
| Middleware | Ghi method/path/status/thời gian xử lý và request ID; không ghi body, query string hoặc thông tin đăng nhập |
| Health | Liveness của API và readiness kiểm tra kết nối database |
| Tests | Unit test và HTTP integration test; integration dùng SQLite trong bộ nhớ |
| GitHub Actions | Chỉ có thư mục giữ chỗ; chưa có workflow CI/CD |
| Users/JWT/RBAC | Đã có Identity, đăng ký Customer, JWT, refresh rotation/reuse detection, logout, xem/sửa họ tên hồ sơ, xác thực riêng email/số điện thoại, đổi và khôi phục mật khẩu, bootstrap Admin và Admin tạo Operator |
| ChargingBays, Batteries, Reservations | Có API khai báo và cập nhật tài nguyên, sửa giờ phục vụ, giữ chỗ, hủy, hết hạn và check-in theo trạm; đồng bộ SignalR thời gian thực |
| Operator Dashboard & Realtime | Giao diện Operator (`/operator/dashboard`) quản lý trụ sạc, pin, giờ phục vụ, check-in mã giữ chỗ; tự động đồng bộ thời gian thực sang bản đồ khách hàng |
| ChargingSessions | Đã có backend: Operator xác nhận xe đỗ, bắt đầu/kết thúc phiên với chỉ số công tơ, xác nhận xe rời hoặc không sạc |
| BatterySwaps, Invoice, Payment | Chưa triển khai |
| Angular / Station Finder | Trang công khai `/`: danh sách phân trang, bản đồ, chi tiết và chỉ đường; có giao diện desktop/mobile |
| Thông báo nghiệp vụ | Chưa triển khai |

Các phần nghiệp vụ bên dưới là mục tiêu phát triển, không phải tính năng đã hoàn thành.

## 2. Quy tắc nghiệp vụ đã chốt

### Địa điểm và dịch vụ

- Một địa điểm có thể cung cấp sạc ô tô, đổi pin xe máy hoặc cả hai.
- Mỗi chỗ sạc ô tô gồm một chỗ đỗ gắn với một đầu sạc.
- Phiên bản đầu hỗ trợ một chuẩn đầu sạc ô tô và một hệ pin xe máy.
- Mỗi lượt đổi pin nhận đúng một pin cũ và giao đúng một pin sẵn sàng.
- Địa điểm chỉ nhận đặt chỗ trong giờ có nhân viên phục vụ; giờ phục vụ có thể khác nhau giữa hai dịch vụ.

### Vai trò

| Vai trò | Quyền dự kiến |
| --- | --- |
| Customer | Tìm địa điểm, đặt/hủy, xem lịch sử và thanh toán |
| Operator | Tại trạm được phân công, sửa giờ phục vụ, cập nhật trạng thái cổng sạc và pin, xác nhận khách đến, ghi nhận sử dụng và báo sự cố |
| Admin | Quản lý trạm và danh mục tài nguyên, thiết lập lịch ban đầu, phân công nhân viên, quản lý giá và xem báo cáo |

### Đặt chỗ và xác nhận thực tế

- Khách giữ một chỗ sạc hoặc một pin cụ thể trong 15 phút để đến ngay; chưa đặt lịch tương lai.
- Một khách chỉ có một lượt đặt đang hiệu lực.
- Hai request đồng thời không được giữ cùng tài nguyên. Quy tắc phải được bảo vệ bằng transaction và ràng buộc database.
- Nhân viên xác nhận khách đến; đã đến chưa đồng nghĩa đã sử dụng dịch vụ.
- Nếu khách đến nhưng không sạc, đóng lượt với lý do phù hợp; không tạo phiên sạc và hóa đơn tiền điện.
- Sạc xong chưa có nghĩa xe đã rời. Chỉ mở chỗ cho khách tiếp theo khi nhân viên xác nhận xe rời.
- Pin cũ nhận về phải được kiểm tra/sạc lại trước khi trở thành pin sẵn sàng.
- Cron/job chỉ hết hạn đặt chỗ và gửi nhắc nhở; không suy đoán khách đã đến, xe đã sạc hay pin đã đầy.
- Tất cả trạng thái vật lý trong phiên bản này do Operator xác nhận; chưa kết nối thiết bị thật.

### Tính phí và trách nhiệm

- Ô tô: `(kWh cuối - kWh đầu) × đơn giá/kWh`. Chỉ số cuối không được nhỏ hơn chỉ số đầu.
- Đổi pin: giá một pin/lượt.
- Lưu đơn giá áp dụng tại thời điểm sử dụng; đổi bảng giá không sửa hóa đơn cũ.
- Thanh toán dự kiến dùng sandbox/mock provider với webhook được xác minh và xử lý idempotency.
- Lịch sử sử dụng và báo cáo sự cố dùng để truy vết. Không tự kết luận khách làm hỏng thiết bị hoặc tự tính bồi thường.

## 3. Cấu trúc mã nguồn

```text
solution-root/
├── backend/                                   # Toàn bộ mã nguồn Backend .NET 9
│   ├── EV Charge & Swap Management System/    # Project Web API ASP.NET Core
│   │   ├── Features/
│   │   │   ├── Authentication/
│   │   │   │   ├── Entities/RefreshToken.cs
│   │   │   │   ├── Persistence/RefreshTokenConfiguration.cs
│   │   │   │   ├── Requests/
│   │   │   │   ├── Responses/AuthenticationResponse.cs
│   │   │   │   ├── Services/
│   │   │   │   │   ├── AuthenticationService.cs
│   │   │   │   │   └── JwtTokenService.cs
│   │   │   │   ├── AuthenticationController.cs
│   │   │   │   └── JwtOptions.cs
│   │   │   ├── Users/
│   │   │   │   ├── Entities/ApplicationUser.cs
│   │   │   │   ├── Persistence/ApplicationUserConfiguration.cs
│   │   │   │   ├── Requests/CreateOperatorRequest.cs
│   │   │   │   ├── Responses/UserResponse.cs
│   │   │   │   ├── Services/
│   │   │   │   │   ├── AdminBootstrapper.cs
│   │   │   │   │   └── UserService.cs
│   │   │   │   └── UsersController.cs
│   │   │   ├── Authorization/
│   │   │   │   ├── Persistence/
│   │   │   │   └── AppRoles.cs
│   │   │   └── Locations/
│   │   │       ├── Entities/Location.cs
│   │   │       ├── Persistence/LocationConfiguration.cs
│   │   │       ├── Requests/
│   │   │       ├── Responses/
│   │   │       ├── Services/LocationService.cs
│   │   │       └── LocationsController.cs
│   │   ├── Common/
│   │   │   ├── Errors/GlobalExceptionHandler.cs
│   │   │   ├── Results/OperationResult.cs
│   │   │   └── Pagination/
│   │   │       ├── PaginationRequest.cs
│   │   │       └── PagedResponse.cs
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── AppDbContextFactory.cs
│   │   │   ├── Migrations/
│   │   │   └── Seed/dev-locations.sql
│   │   ├── Health/
│   │   │   ├── DatabaseHealthCheck.cs
│   │   │   └── HealthResponseWriter.cs
│   │   ├── Middleware/RequestLoggingMiddleware.cs
│   │   ├── Properties/launchSettings.json
│   │   ├── Program.cs
│   │   └── appsettings*.json
│   ├── tests/EVChargeSwap.Tests/              # Project kiểm thử Backend
│   │   ├── Unit/
│   │   └── Integration/
│   ├── Dockerfile                             # Build API và image migration local
│   ├── .dockerignore                          # Loại secret/build output khỏi build context
│   └── EV Charge & Swap Management System.sln # File solution Backend
├── frontend/                                  # Ứng dụng web Frontend (Angular SPA)
│   ├── Dockerfile                             # Build Angular và chạy trên Nginx Alpine
│   ├── nginx.conf                             # Cấu hình Nginx hỗ trợ SPA client-side routing
│   ├── .dockerignore                          # Bỏ qua node_modules và build cache
│   └── README.md                              # Hướng dẫn khởi tạo và phát triển Angular
├── docs/                                      # Tài liệu yêu cầu và kiến trúc
├── .github/workflows/.gitkeep                 # Thư mục CI/CD workflows
├── .env.example                               # Mẫu biến môi trường
├── .env                                       # Biến môi trường local (Git bỏ qua)
├── compose.yaml                               # Điều phối Postgres, Backend API và Frontend
├── .editorconfig
├── .gitignore
└── README.md
```

### Trách nhiệm từng loại file

| File | Trách nhiệm |
| --- | --- |
| Entity, ví dụ `Location.cs` | Dữ liệu nghiệp vụ và các điều kiện tạo đối tượng hợp lệ |
| `Entities/*` | Entity được EF Core map với database; có thể chứa hành vi chỉ thay đổi trạng thái của chính entity |
| `Persistence/*` | Fluent mapping giữa entity và bảng/cột/index/foreign key; không xử lý HTTP hoặc use case |
| `Services/*` | Xử lý use case; Auth và User được tách để tránh gom nhiều trách nhiệm vào một service |
| Request DTO, ví dụ `PaginationRequest.cs` | Dữ liệu nhận từ HTTP; validation tham số đầu vào |
| Response DTO, ví dụ `LocationResponse.cs` | Dữ liệu chọn lọc trả cho Angular, không trả nguyên entity |
| `LocationService.cs` | Danh sách/chi tiết trạm, tìm theo bán kính và thao tác quản trị; truy vấn công khai chỉ lấy trạm đang hiển thị |
| `LocationsController.cs` | Nhận request, gọi service và trả HTTP response |
| `LocationConfiguration.cs` | Mapping tên cột, precision, độ dài, index và check constraint |
| `AppDbContext.cs` | Điểm truy vấn/lưu dữ liệu của EF Core |
| `AppDbContextFactory.cs` | Hỗ trợ công cụ EF; đọc user-secrets hoặc biến môi trường khi cập nhật database |
| `Data/Migrations/*` | Lịch sử schema do EF sinh; review trước khi áp dụng |
| `Common/Errors/*` | Trả Problem Details cho lỗi bất ngờ, không đưa stack trace hoặc secret vào response |
| `Common/Results/*` | Kết quả use case dùng chung, giúp service không phụ thuộc ngược vào một feature khác |
| `Health/*` | Phân biệt process đang sống với database có thể kết nối |
| `Middleware/*` | Hành vi HTTP dùng chung; không đặt nghiệp vụ đặt chỗ/sạc ở đây |
| `Program.cs` | Đăng ký dependency injection và sắp thứ tự middleware/endpoints |

Luồng đọc hiện tại: Angular/Swagger UI → Controller → Service → AppDbContext → PostgreSQL → Response DTO → JSON.

## 4. Điều kiện chạy local

- .NET 9 SDK và một IDE hỗ trợ .NET 9.
- EF Core CLI tương thích EF Core 9 để quản lý migration.
- Docker Desktop đang chạy nếu dùng PostgreSQL qua Compose; hoặc một PostgreSQL server riêng.
- Node.js 22.12+ hoặc 24 và npm để chạy frontend; Angular CLI đã có trong dependency của project.

Các lệnh bên dưới chạy trong **thư mục chứa file solution**.

## 5. Cấu hình .env và connection string

`.env.example` là mẫu được commit để người khác biết các biến cần thiết. `.env` chứa giá trị local và bị `.gitignore` loại. Máy hiện tại đã được tạo một `.env` với mật khẩu local ngẫu nhiên; clone mới cần sao chép mẫu rồi đổi mật khẩu.

```powershell
# Chỉ sao chép khi chưa có .env, tránh ghi đè mật khẩu hiện tại.
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
```

**Docker Compose tự đọc .env; ASP.NET Core không tự đọc file này.** Đặt `POSTGRES_PASSWORD`, một `JWT_SIGNING_KEY` ngẫu nhiên dài ít nhất 32 ký tự và, nếu cần, thông tin `ADMIN_BOOTSTRAP_*` tùy chọn theo `.env.example`.

API chạy trên host nhận connection string và khóa JWT từ user-secrets hoặc biến môi trường. Để dùng cùng mật khẩu với Docker local và tạo khóa JWT riêng:

```powershell
$apiProject = '.\backend\EV Charge & Swap Management System\EV Charge & Swap Management System.csproj'
$passwordLine = Get-Content .env | Where-Object { $_ -match '^POSTGRES_PASSWORD=' } | Select-Object -First 1
if (-not $passwordLine) { throw 'Missing POSTGRES_PASSWORD in .env' }
$localPassword = ($passwordLine -split '=', 2)[1]
dotnet user-secrets set 'ConnectionStrings:Default' "Host=localhost;Port=5432;Database=ev_charge_swap;Username=ev_user;Password=$localPassword" --project $apiProject
$jwtSigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set 'Jwt:SigningKey' $jwtSigningKey --project $apiProject
```

Tùy chọn tạo Admin đầu tiên sau khi migration đã được áp dụng:

```powershell
dotnet user-secrets set 'BootstrapAdmin:Email' 'admin@example.local' --project $apiProject
dotnet user-secrets set 'BootstrapAdmin:FullName' 'Local Administrator' --project $apiProject
dotnet user-secrets set 'BootstrapAdmin:Password' 'ChangeThisPassword123' --project $apiProject
```

Sau lần khởi động đầu tiên, có thể xóa ba secret bootstrap; tài khoản Admin đã nằm trong database. Không dùng mật khẩu ví dụ ở môi trường thật.

Ở môi trường triển khai, cung cấp `ConnectionStrings__Default` qua hệ thống secret của môi trường. Không commit mật khẩu, khóa ký JWT hoặc khóa thanh toán.

## 6. Chạy PostgreSQL và API

### 6.1. Chạy toàn bộ bằng Docker

Mở Docker Desktop, rồi tại thư mục solution chạy:

```powershell
docker compose up -d --build
```

Compose build image từ Dockerfile, chờ PostgreSQL healthy, chạy service `migrate` để áp dụng EF Core migration, rồi mới khởi động `api`. `migrate` thoát với mã 0 sau khi thành công là bình thường. API không tự chạy migration trong mỗi lần khởi động.

Container local dùng tên theo quy ước `project-service-environment`:

| Service | Container |
| --- | --- |
| api | `ev-charge-swap-api-dev` |
| postgres | `ev-charge-swap-postgres-dev` |
| migrate | `ev-charge-swap-migrate-dev` |
| frontend | `ev-charge-swap-frontend-dev` |

Các lệnh `docker compose logs/exec` vẫn dùng tên service `api`, `postgres`, `migrate`, `frontend`.

- Frontend: `http://localhost:4200`
- Swagger UI: `http://localhost:8080/swagger`
- API: `http://localhost:8080/api/locations`
- Readiness: `http://localhost:8080/health/ready`

Không cần .NET SDK hoặc user-secrets trên máy host cho cách này: Compose truyền connection string từ `.env` vào container. Trong mạng Docker, database có hostname `postgres`; API chạy trên máy host dùng `localhost`.

Nếu muốn tách bước build và chạy:

```powershell
docker compose build api migrate
docker compose up -d
docker compose ps --all
docker compose logs -f api
```

Dockerfile dùng stage SDK để restore/publish; image API cuối chỉ chứa ASP.NET runtime và ứng dụng publish, chạy bằng user không phải root. Stage `migration` dùng SDK + EF CLI để hỗ trợ database local. `.env`, user-secrets, chứng chỉ và thư mục build không được đưa vào build context.

Đây là cấu hình **Development local**: HTTP cổng 8080, Swagger UI được bật và HTTPS redirection được tắt qua `HttpsRedirection__Enabled=false`. Cấu hình triển khai Production sẽ cần môi trường, secret và TLS phù hợp; không dùng nguyên cấu hình local làm production.

Dừng stack bằng `docker compose down`; lệnh này giữ dữ liệu trong volume. Khi thay đổi code/migration, chạy lại `docker compose up -d --build`.

### 6.2. Chạy API trên máy bằng Visual Studio/dotnet

Cách này vẫn dùng PostgreSQL trong Docker nhưng API chạy ở máy host. Thực hiện bước user-secrets ở mục 5 trước.

```powershell
$apiProject = '.\backend\EV Charge & Swap Management System\EV Charge & Swap Management System.csproj'
docker compose up -d postgres
dotnet restore '.\backend\EV Charge & Swap Management System.sln'
dotnet ef database update --project $apiProject --startup-project $apiProject
dotnet run --project $apiProject --launch-profile https
```

Nếu máy chưa có EF CLI, cài bằng `dotnet tool install --global dotnet-ef --version 9.0.15`. Nếu chứng chỉ HTTPS local chưa được tin cậy, dùng `dotnet dev-certs https --trust`.

Migration không tự chạy khi API khởi động. Container dùng volume `postgres_data` để giữ dữ liệu. Đổi mật khẩu trong `.env` không tự đổi mật khẩu role của database đã được khởi tạo; cần cập nhật database và connection string cho khớp.

### 6.3. Dashboard khách hàng (Angular)

Từ root repo, khởi động API/PostgreSQL (cần `.env` ở mục 5):

```powershell
docker compose up -d --build postgres migrate api
cd frontend
npm ci
npm start
```

Mở **http://localhost:4200/**. Không cần đăng nhập để xem trạm. Nếu frontend Docker đang chiếm cổng 4200, dùng `npm start -- --port 4300` và mở http://localhost:4300/.

- Cấu hình URL API và tile map: `frontend/src/app/features/station-finder/station-finder.config.ts`. Mặc định API là `/api`.
- Khi chạy Angular local, `frontend/proxy.conf.json` chuyển `/api/**` sang `http://localhost:8080`. Nếu chạy API bằng Visual Studio, đổi target theo cổng của API.
- Khi chạy Docker Compose, Nginx chuyển `/api/` sang `api:8080`; đường dẫn SPA vẫn trả về `index.html`. Build frontend dùng `npm ci` và output Angular `dist/*/browser`.
- Nạp seed demo theo mục 8: 10 trạm trọng điểm mẫu hoặc 100 trạm sạc & đổi pin thực tế trải khắp các đô thị toàn quốc (TP.HCM, Hà Nội, Đà Nẵng, Quy Nhơn, Hải Phòng, Bình Dương, Cần Thơ, Nha Trang, Vũng Tàu...). Sau đó tải lại dashboard; nút **Tải thêm địa điểm** lấy 20 trạm mỗi lần. Danh sách và marker luôn dùng chung tập dữ liệu, loại trùng theo ID.
- Feature nằm trong `features/station-finder/`: page điều phối trạng thái, `station-list/` hiển thị danh sách, `station-map/` quản lý Leaflet, `services/` gọi API và `models/` định nghĩa DTO. Chọn thẻ hoặc marker mở chi tiết từ API; Chỉ đường mở Google Maps bằng tọa độ đích.
- Header dùng logo con hươu trong `frontend/public/favicon.png`. Danh sách có thể đóng/mở; khi chọn trạm, bản đồ giữ marker trong phần nhìn thấy, kể cả khi bảng chi tiết đang mở trên mobile. Mục Đặt chỗ/Lịch sử được ghi “Sắp có” trong giao diện; backend đặt chỗ đã có API riêng và chưa nối với nút Đặt chỗ.
- Trạng thái trạm do Admin cập nhật qua trường `operatingStatus` khi `POST/PUT /api/locations`: `Unknown`, `Open`, `Closed`, `Maintenance`. Trạm cũ mặc định `Unknown` (giao diện ghi “Chưa có thông tin”). `isActive=false` ẩn trạm khỏi API công khai, còn `Closed`/`Maintenance` vẫn hiển thị với nhãn tương ứng. Trạng thái này không tự thay đổi theo giờ; giờ phục vụ và tình trạng thiết bị là dữ liệu riêng ở mốc sau.
- Dữ liệu demo có lịch phục vụ 24/7 (`isOpen24Hours=true`), công suất sạc `chargingPowerKw` thuộc 30/60/150/250 kW nếu có sạc ô tô, và `batteryCapacityKwh=1.5` cho dịch vụ đổi pin LFP. Seed phân bổ thông số và trạng thái giả lập ổn định theo ID; đây không phải trạng thái thiết bị hay số pin còn thực tế. `Closed`/`Maintenance` là ngoại lệ tạm thời của lịch 24/7.
- Thanh tìm kiếm tra tên hoặc địa chỉ trạm trên toàn bộ database, đồng thời cho phép tìm địa danh. Giao diện không còn nút chọn bán kính. Khi duyệt bản đồ, dữ liệu được tải trong phạm vi 50 km quanh tâm vùng đang xem bằng API nearby. Có lọc dịch vụ và hai bộ lọc khả dụng: **Đang mở** hoặc **Còn chỗ**. Chưa có giá, đặt chỗ hoặc lịch sử.
- Khi bấm Enter hoặc nút **Tìm**, thanh tìm kiếm còn gửi một truy vấn địa danh rõ ràng (ví dụ `Bình Sơn`) tới Nominatim/OpenStreetMap và cho người dùng chọn kết quả đầy đủ tỉnh/thành trước khi tải trạm quanh điểm đó. Không gọi theo từng ký tự. Nominatim công khai chỉ phù hợp cho demo có lưu lượng nhỏ; production cần nhà cung cấp geocoding có SLA hoặc một dịch vụ tự quản lý.

Kiểm tra frontend (tại `frontend/`):

```powershell
npm run build
npm test -- --watch=false
npm run test:e2e
# Thêm kiểm tra dữ liệu thật khi API cổng 8080 và PostgreSQL đã có seed:
$env:LIVE_API = '1'
npm run test:e2e
Remove-Item Env:LIVE_API
```

Browser test dùng Microsoft Edge đã cài, tự mở Angular cổng 4300, chạy desktop và mobile. Nếu máy không có Edge, cài Edge hoặc đổi `channel` trong `playwright.config.ts` sang browser đã cài. Test mặc định dùng API giả lập để kiểm tra lỗi/rỗng, tải thêm, chọn từ hai phía và tọa độ chỉ đường; `LIVE_API=1` kiểm tra cả trang thứ hai từ API thật. Ảnh kiểm tra được lưu dưới `frontend/test-results/` (không commit).

## 7. Swagger UI, API và health endpoints

| Endpoint local | Mục đích |
| --- | --- |
| `https://localhost:7195/swagger` | Swagger UI để đọc tài liệu và gọi thử API |
| `https://localhost:7195/openapi/v1.json` | Tài liệu OpenAPI JSON |
| `GET /api/locations?page=1&pageSize=20` | Danh sách địa điểm đang hoạt động |
| `GET /api/locations/{id}` | Chi tiết địa điểm, 404 khi không có/đã ẩn |
| `GET /api/locations/nearby?latitude=13.766667&longitude=109.233333&radiusKm=10&service=charging&availability=available` | Tìm trạm trong bán kính, lọc dịch vụ/tên/địa chỉ; `availability` nhận `all`, `open`, hoặc `available` (đang mở và còn tài nguyên cho dịch vụ đã chọn). Mặc định ưu tiên trạm còn chỗ, rồi trạm đang mở, sau đó mới theo khoảng cách. |
| `GET /api/locations/admin` | Admin xem cả trạm đang ẩn |
| `POST /api/locations` | Admin tạo trạm |
| `PUT /api/locations/{id}` | Admin sửa tên, địa chỉ, tọa độ và dịch vụ |
| `PATCH /api/locations/{id}/visibility` | Admin ẩn/hiện trạm bằng `{ "isActive": false/true }` |
| `POST /api/auth/register` | Đăng ký Customer với họ tên, email, số điện thoại và mật khẩu; trả 202, chưa cấp token |
| `POST /api/auth/verification/confirm` | Xác minh email hoặc số điện thoại bằng mã 6 chữ số |
| `POST /api/auth/verification/resend` | Gửi lại mã sau 60 giây; trả 202 kể cả khi email không tồn tại |
| `POST /api/auth/verification/me/send` | Người đang đăng nhập gửi mã xác thực cho email hoặc số điện thoại của chính mình |
| `POST /api/auth/verification/me/confirm` | Người đang đăng nhập xác nhận một kênh bằng mã 6 số |
| `GET /api/auth/development/code?channel=email&destination=...` | Chỉ trong Development trên loopback; xem mã thử nghiệm khi chưa có nhà cung cấp gửi thật |
| `POST /api/auth/login` | Đăng nhập bằng email hoặc số điện thoại đã xác minh và mật khẩu; khóa 15 phút sau 5 lần sai |
| `POST /api/auth/refresh` | Rotate refresh token và cấp access token mới |
| `POST /api/auth/logout` | Thu hồi refresh token; gọi lặp lại vẫn trả 204 |
| `GET /api/users/me` | Hồ sơ tài khoản hiện tại; yêu cầu Bearer token |
| `PUT /api/users/me` | Đổi họ tên của tài khoản hiện tại; yêu cầu Bearer token |
| `POST /api/users/me/change-password` | Đổi mật khẩu bằng mật khẩu cũ; vô hiệu hóa mọi phiên cũ |
| `POST /api/auth/password/forgot` | Yêu cầu mã khôi phục qua email/số điện thoại đã xác minh; luôn trả 202 với request hợp lệ |
| `POST /api/auth/password/reset` | Đặt mật khẩu mới bằng mã 6 số; vô hiệu hóa mọi phiên cũ |
| `POST /api/users/operators` | Admin tạo tài khoản Operator |
| `GET /health/live` | Process API đang phản hồi; không kiểm tra database |
| `GET /health/ready` | Kiểm tra kết nối database; 503 nếu không sẵn sàng |

Swagger UI và OpenAPI chỉ được mở trong **Development**. Khi đăng ký, nhập số di động Việt Nam (`09...` hoặc `+849...`). Lấy mã từ endpoint Development trên loopback, xác minh kênh `email` hoặc `phone`, rồi gọi `POST /api/auth/login` với `identifier` (email hoặc số điện thoại) và `password`. Để gọi endpoint cần đăng nhập, chọn **Authorize** và dán `accessToken` (Swagger tự thêm tiền tố `Bearer`). Không dùng endpoint lấy mã Development trong môi trường thật.

Health endpoints kiểm tra sức khỏe hệ thống backend.

Logout thu hồi refresh token của phiên hiện tại. Access token đã phát hành tiếp tục hợp lệ tối đa 15 phút; mọi request có token vẫn kiểm tra tài khoản còn active và `SecurityStamp` chưa thay đổi. Refresh token có hạn tối đa 30 ngày từ lúc đăng nhập; việc xoay token không gia hạn mốc này. Chưa cấu hình nhà cung cấp email/SMS Production nên đăng ký thật sẽ báo lỗi cấu hình thay vì giả vờ đã gửi mã.

Hồ sơ khách hàng tại `http://localhost:4200/profile`: xem email, số điện thoại, trạng thái xác thực từng kênh, gửi/nhập mã và sửa họ tên. Hai kênh đăng nhập chỉ đọc; thay đổi địa chỉ email hoặc số điện thoại sẽ cần một luồng xác minh thay đổi riêng. Màn hình `http://localhost:4200/forgot-password` nhận email hoặc số điện thoại đã xác minh, gửi mã 6 số có hạn 5 phút (tối đa 5 lần nhập sai), rồi đặt mật khẩu mới. Có thể yêu cầu gửi lại sau 60 giây. Trong Development local, lấy mã bằng `GET /api/auth/development/code?channel=email&destination=<email>` hoặc `channel=phone`; Production dùng Resend và Twilio như cấu hình bên dưới. Đổi hoặc đặt lại mật khẩu yêu cầu đăng nhập lại trên mọi thiết bị. Chức năng này dùng bảng `verification_challenges` hiện có, không cần migration mới.

### Gửi mã xác thực trong Production

Không ghi key vào `appsettings.json` hoặc Git. Cấp các biến môi trường sau cho API:

```text
VerificationDelivery__ResendApiKey=<Resend API key>
VerificationDelivery__EmailFrom=EVGo <verify@ten-mien-da-xac-minh.vn>
VerificationDelivery__TwilioAccountSid=<Twilio Account SID>
VerificationDelivery__TwilioAuthToken=<Twilio Auth Token>
VerificationDelivery__TwilioSenderId=EVGo
```

Resend yêu cầu xác minh domain gửi email. SMS đến số Việt Nam yêu cầu đăng ký trước Alphanumeric Sender ID và mẫu OTP với nhà mạng; giá trị `TwilioSenderId` phải khớp chính xác Sender ID đã được duyệt. Khi provider từ chối request, API trả 503 và xóa mã vừa tạo để khách có thể thử gửi lại. Log không ghi mã OTP, địa chỉ email, số điện thoại hoặc provider credential.

`Page >= 1`, `1 <= PageSize <= 100`. Request tạo offset vượt giới hạn số nguyên bị từ chối bằng HTTP 400. Kết quả phân trang có `items`, `page`, `pageSize`, `totalCount`.

Các response lỗi dùng Problem Details. Middleware thêm header `X-Request-ID`; lỗi có `traceId` để đối chiếu log. Request logging không ghi query string, request/response body hoặc thông tin đăng nhập.

## Đặt chỗ qua API

Backend hỗ trợ giữ chỗ sạc ô tô và giữ một pin xe máy trong 15 phút. Seed địa điểm phục vụ demo bản đồ. Để đặt được tại một trạm, Admin cần đặt trạng thái vận hành `Open` và tạo tài nguyên cùng giờ phục vụ ban đầu cho dịch vụ tương ứng. Operator được phân công có thể sửa giờ phục vụ và cập nhật trạng thái từng cổng hoặc pin.

| Vai trò | Endpoint | Body hoặc tham số |
| --- | --- | --- |
| Admin | `POST /api/locations/{locationId}/charging-bays` | `{ "code": "A01", "powerKw": 60 }` |
| Admin | `POST /api/locations/{locationId}/batteries` | `{ "serial": "PIN-001" }`, chỉ đăng ký pin đã sẵn sàng giao |
| Admin | `POST /api/locations/{locationId}/service-hours` | `{ "serviceType": 1, "day": 1, "opensAtMinute": 480, "closesAtMinute": 1200 }` |
| Admin | `POST /api/locations/{locationId}/operators` | `{ "operatorId": "<id>" }` |
| Admin | `PUT /api/operators/{operatorId}/assignment` | `{ "locationId": "<id>" }` điều chuyển |
| Admin | `DELETE /api/operators/{operatorId}/assignment` | Kết thúc phân công hiện tại |
| Admin | `GET /api/operators/{operatorId}/assignments` | Xem lịch sử phân công |
| Operator | `GET /api/operators/me/location` | Xem trạm hiện đang phụ trách |
| Operator | `GET /api/operator/me/station` | Xem cổng sạc, pin, giờ phục vụ và lượt đặt hiện tại của trạm |
| Operator | `PATCH /api/operator/me/charging-bays/{id}/equipment-state` | `{ "state": 2 }` để báo cổng lỗi; 1 Active, 2 Faulted, 3 Maintenance |
| Operator | `PATCH /api/operator/me/batteries/{id}/state` | `{ "state": 5 }` để báo pin lỗi; chuyển trạng thái theo quy tắc pin |
| Operator | `PUT /api/operator/me/service-hours` | `{ "serviceType": 1, "day": 1, "intervals": [{ "opensAtMinute": 480, "closesAtMinute": 1200 }] }` |
| Customer | `POST /api/reservations` | `{ "locationId": "<id>", "serviceType": 1 }` |
| Customer | `GET /api/reservations/active` hoặc `GET /api/reservations/{id}` | Xem lượt của chính mình |
| Customer | `POST /api/reservations/{id}/cancel` | Hủy lượt còn hạn và chưa check-in |
| Operator | `POST /api/reservations/check-in` | `{ "locationId": "<id>", "code": "<mã từ lượt đặt>" }` |
| Operator | `POST /api/reservations/{id}/close-without-service?locationId=<id>` | Xác nhận khách không sử dụng dịch vụ và chỗ đỗ đã trống nếu là lượt sạc |
| Operator | `POST /api/operator/me/charging-reservations/{id}/vehicle-arrived` | Xác nhận xe đã đỗ, chuyển bay sang `Occupied` |
| Operator | `GET /api/operator/me/charging-reservations/{id}` | Xem trạng thái xe và phiên sạc của lượt tại trạm được phân công |
| Operator | `POST /api/operator/me/charging-reservations/{id}/start` | `{ "meterKwh": 120.5 }` chỉ số công tơ bắt đầu |
| Operator | `POST /api/operator/me/charging-reservations/{id}/complete` | `{ "meterKwh": 135.7 }` chỉ số kết thúc, bay vẫn `Occupied` |
| Operator | `POST /api/operator/me/charging-reservations/{id}/no-charge` | Xác nhận xe đã đỗ nhưng không sạc; không tạo phiên sạc |
| Operator | `POST /api/operator/me/charging-reservations/{id}/vehicle-departed` | Sau khi kết thúc sạc hoặc xác nhận không sạc, đóng lượt và chuyển bay sang `Vacant` |
| Customer | `GET /api/reservations/{id}/charging-session` | Xem phiên sạc của chính mình |

`serviceType` là `1` cho sạc ô tô và `2` cho đổi pin. `day` dùng `0` cho Chủ nhật, `1` cho Thứ hai đến `6` cho Thứ bảy. Giờ phục vụ tính theo phút từ 00:00 theo giờ Việt Nam. Ví dụ `480–1200` là 08:00–20:00, còn `0–1440` là cả ngày. Tạo lịch riêng cho từng dịch vụ và từng ngày. Lịch qua đêm được nhập thành hai khoảng ở hai ngày.

API chọn tài nguyên trong transaction PostgreSQL. Mỗi khách và mỗi tài nguyên chỉ có tối đa một lượt `Held` hoặc `CheckedIn`. Nếu trạm ngừng nhận đặt, khách đang có lượt hiệu lực hoặc tài nguyên đã hết, API trả `409`. Job chỉ chuyển lượt `Held` quá hạn sang `Expired`. Thời điểm hết hạn được kiểm tra khi hủy hoặc check-in, kể cả khi job chưa chạy.
API công khai hiện tính số cổng và pin khả dụng từ bảng tài nguyên cùng lượt giữ đang hoạt động. Các giá trị `available_charging_bays` và `available_batteries` cũ trong bảng `locations` không còn quyết định số hiển thị; dữ liệu seed chỉ có Location sẽ hiển thị 0 cho đến khi Admin khai báo cổng và pin. `OperatorResourceUpdates` thêm thông tin người và thời điểm cập nhật. Lượt đã giữ vẫn tồn tại khi Operator báo lỗi tài nguyên; xử lý chuyển hoặc hủy lượt đó thuộc luồng sự cố tiếp theo. Việc đổi giờ chỉ ảnh hưởng lượt mới.

Quan hệ dữ liệu gồm một `Location` có nhiều `ChargingBay`, `Battery` và `ServiceHours`. Mỗi Operator có tối đa một `OperatorAssignment` đang hoạt động, còn một trạm có thể có nhiều Operator. Các bản ghi phân công đã kết thúc được giữ làm lịch sử. Mỗi `Reservation` trỏ tới một Customer, một Location và đúng một trong hai tài nguyên.

`AddReservations` tạo các bảng backend. `OperatorAssignmentHistory` bổ sung lịch sử và ràng buộc mỗi Operator chỉ có một phân công đang hoạt động. `ChargingLifecycle` thêm mốc xe đến/rời, xác nhận không sạc và bảng `charging_sessions`; các cột mới trên reservation đều nullable để giữ dữ liệu cũ. Mỗi reservation có tối đa một phiên sạc, công tơ kết thúc phải lớn hơn hoặc bằng công tơ đầu. Chạy migration theo mục 6 trước khi gọi API. Để chạy test migration và tranh chỗ, đặt biến `EV_RESERVATIONS_TEST_DATABASE` thành connection string của **database PostgreSQL riêng cho test**, rồi chạy lệnh ở mục 9. Test này tự áp dụng migration.

Frontend cho phiên sạc, giao nhận pin thực tế, tính phí và thanh toán thuộc các bước triển khai tiếp theo.

## 8. Dữ liệu demo

Sau khi áp dụng migration, có thể thêm 10 trạm trọng điểm minh họa tại TP.HCM, Hà Nội, Đà Nẵng và Quy Nhơn vào database local:

```powershell
Get-Content -Raw '.\backend\EV Charge & Swap Management System\Data\Seed\dev-locations.sql' | docker exec -i ev-charge-swap-postgres-dev psql -v ON_ERROR_STOP=1 -U ev_user -d ev_charge_swap
```

Seed nhỏ gán ba trạng thái mẫu `Open`, `Closed`, `Maintenance` cho ba bản ghi đầu để thử giao diện. Đây là dữ liệu minh họa, không xác nhận tình trạng vận hành của địa điểm ngoài đời. Chạy migration `AddLocationOperatingStatus` trước khi nạp seed này.

Để thử bản đồ và tìm trạm theo bán kính trên tập dữ liệu lớn hơn, có thêm seed **100 địa điểm sạc & đổi pin giả lập** trải dài nhiều đô thị (TP.HCM, Hà Nội, Đà Nẵng, Quy Nhơn, Hải Phòng, Quảng Ninh, Bình Dương, Đồng Nai, Cần Thơ, Nha Trang, Vũng Tàu, Đà Lạt...). Tọa độ đặt gần các địa danh làm mốc thử bản đồ; tên trạm và tình trạng vận hành không xác nhận có cơ sở EVGo thật tại đó. Dữ liệu gồm ba loại: vừa sạc vừa đổi pin, chỉ sạc ô tô điện, và chỉ đổi pin xe máy điện:

```powershell
Get-Content -Raw '.\backend\EV Charge & Swap Management System\Data\Seed\dev-national-charging-locations.sql' | docker exec -i ev-charge-swap-postgres-dev psql -v ON_ERROR_STOP=1 -U ev_user -d ev_charge_swap
```

## 9. Chạy test

```powershell
dotnet test '.\backend\EV Charge & Swap Management System.sln' -m:1
```

- Unit test kiểm tra quy tắc Location, giới hạn tọa độ/độ dài, bắt buộc có dịch vụ và validation/overflow của phân trang.
- HTTP integration test dùng `WebApplicationFactory` để chạy API trong test server và SQLite trong bộ nhớ để kiểm tra truy vấn, phân trang, lọc địa điểm đã ẩn và 404.
- Test nền tảng kiểm tra liveness/readiness, tình huống dependency thất bại, Swagger UI/OpenAPI chỉ xuất hiện trong Development, lỗi không lộ thông tin và request ID khớp.
- Test auth kiểm tra xác minh từng kênh, mã dùng một lần/khóa sau 5 lần sai, đăng nhập qua email hoặc số điện thoại, hạn phiên 30 ngày, refresh rotation và reuse detection, logout idempotent cùng quyền Admin tạo Operator.
- Không cần Docker để chạy bộ test này. SQLite không chứng minh PostgreSQL migration hay constraint hoạt động giống hoàn toàn; kiểm thử PostgreSQL thật là bước riêng khi có Docker/database test.
- Test project không được dùng làm nơi chứa code production. SQLite `EnsureCreated` chỉ dùng để tạo database test tạm thời; database ứng dụng dùng migrations.

## 10. Thay đổi schema và quy ước code

```powershell
$apiProject = '.\backend\EV Charge & Swap Management System\EV Charge & Swap Management System.csproj'
dotnet ef migrations add TenMigration --project $apiProject --startup-project $apiProject --output-dir Data/Migrations
dotnet ef migrations script --idempotent --project $apiProject --startup-project $apiProject
```

Review migration và SQL trước khi áp dụng. Không dùng `EnsureCreated()` cho PostgreSQL ứng dụng.

Mỗi feature giữ entity, DTO, service, controller và mapping gần nhau. Controller chỉ điều phối HTTP, service xử lý use case, entity giữ điều kiện nghiệp vụ. Chỉ thêm abstraction, Common helper hoặc middleware khi có nhu cầu cụ thể; không tạo file rỗng để đủ tầng. Cấu hình CORS local hiện cho phép Angular tại `http://localhost:4200`.

## 11. GitHub CI/CD

`.github/workflows/.gitkeep` chỉ để Git lưu thư mục. **Chưa có file YAML, pipeline, deploy hoặc secret GitHub nào được tạo.**

Khi tự viết CI, có thể đặt workflow build/test trong thư mục này; CD sẽ phụ thuộc môi trường triển khai bạn chọn. Giữ secret bằng GitHub Secrets/environment của nơi deploy, không ghi vào YAML.

## 12. ERD và lộ trình tiếp theo

Chín entity lõi dự kiến: `User`, `Location`, `ChargingBay`, `Battery`, `Reservation`, `ChargingSession`, `BatterySwap`, `Invoice`, `Payment`. Hiện đã triển khai `User`, `Location`, `ChargingBay`, `Battery`, `Reservation` và `ChargingSession`, cùng entity hạ tầng `RefreshToken`.

1. Đã thêm API khai báo chỗ sạc, pin, phân công nhân viên và giờ phục vụ.
2. Đã thêm API đặt chỗ 15 phút, chống đặt trùng, job hết hạn và check-in.
3. Đã thêm backend phiên sạc và xác nhận xe rời; tiếp theo là giao nhận pin và lịch sử sự cố.
4. Thêm bảng giá, hóa đơn, payment webhook và test đồng thời/idempotency.
5. Mở rộng Angular với tìm kiếm/bán kính, đặt chỗ, lịch sử, thông báo và báo cáo; bản đồ/chỉ đường cơ bản đã có.

Bảng mở rộng như Tariff, OperatorAssignment, ServiceHours, IncidentReport và AuditLog được thêm theo chức năng thực tế. Chưa triển khai đặt lịch tương lai, nhiều hệ pin, tính phí đỗ quá giờ hoặc kết nối phần cứng.

## Tài liệu tham khảo

- [Swagger UI với OpenAPI tích hợp của ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/using-openapi-documents?view=aspnetcore-9.0)
- [ASP.NET Core health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-9.0)
- [ASP.NET Core integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-9.0)
- [EF Core migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [Npgsql EF Core provider](https://www.npgsql.org/efcore/)
- [Containerize a .NET application](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container)
- [Compose startup order](https://docs.docker.com/compose/how-tos/startup-order/)
