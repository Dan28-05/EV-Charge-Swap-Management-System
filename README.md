# EV Charge & Swap Management System

Nền tảng quản lý địa điểm sạc ô tô điện và đổi pin xe máy điện, phục vụ tìm địa điểm trên bản đồ, giữ chỗ, ghi nhận sử dụng dịch vụ, tính phí và thanh toán.

Backend dùng **ASP.NET Core .NET 9 + EF Core + PostgreSQL**, tổ chức thành một ứng dụng monolith chia thư mục theo chức năng. Frontend là **Angular SPA**, dùng Leaflet và OpenStreetMap cho bản đồ.

Tài liệu nguồn của dự án:

- [Product requirements](docs/requirements.md)
- [Architecture and technology roadmap](docs/architecture-roadmap.md)

## 1. Trạng thái triển khai

| Hạng mục | Hiện trạng |
| --- | --- |
| Locations | Đã có entity, mapping EF Core, service, response DTO và API đọc/phân trang |
| PostgreSQL | Đã có cấu hình provider, migration đầu tiên, Docker Compose và seed local tùy chọn |
| Redis | Container Redis 7 Alpine, cache thông tin trạm với explicit invalidation và fallback tự động |
| API trong Docker | Có Dockerfile multi-stage, runtime non-root và job migration riêng cho local |
| Swagger UI | Có tại `/swagger`, chỉ trong Development |
| Common | Đã có phân trang dùng chung và xử lý lỗi không lộ thông tin nội bộ |
| Middleware | Ghi method/path/status/thời gian xử lý và request ID; không ghi body, query string hoặc thông tin đăng nhập |
| Health | Liveness của API và readiness kiểm tra kết nối database |
| Tests | Unit test và HTTP integration test; integration dùng SQLite trong bộ nhớ |
| GitHub Actions | Chỉ có thư mục giữ chỗ; chưa có workflow CI/CD |
| Users/JWT/RBAC | Đã có Identity, đăng ký Customer, JWT, refresh rotation/reuse detection, logout, hồ sơ hiện tại, bootstrap Admin và Admin tạo Operator |
| ChargingBays, Batteries, Reservations | Chưa triển khai |
| ChargingSessions, BatterySwaps, Invoice, Payment | Chưa triển khai |
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
| Operator | Xác nhận khách đến, ghi nhận sạc/giao nhận pin, xác nhận xe rời và báo sự cố tại địa điểm được phân công |
| Admin | Quản lý mạng lưới, nhân viên, tài nguyên, giá và báo cáo |

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
- Mốc này chỉ duyệt danh sách, chọn trạm, xem chi tiết và chỉ đường. Chưa có tìm tên/bán kính, vị trí hiện tại, giá, tồn kho, đặt chỗ hoặc lịch sử. Bản đồ cần Internet để tải tile OpenStreetMap và luôn hiển thị attribution.

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
| `GET /api/locations/nearby?latitude=13.766667&longitude=109.233333&radiusKm=10&service=charging` | Tìm trạm trong bán kính, lọc dịch vụ/tên/địa chỉ, phân trang theo khoảng cách |
| `GET /api/locations/admin` | Admin xem cả trạm đang ẩn |
| `POST /api/locations` | Admin tạo trạm |
| `PUT /api/locations/{id}` | Admin sửa tên, địa chỉ, tọa độ và dịch vụ |
| `PATCH /api/locations/{id}/visibility` | Admin ẩn/hiện trạm bằng `{ "isActive": false/true }` |
| `POST /api/auth/register` | Đăng ký Customer với họ tên, email, số điện thoại và mật khẩu; trả 202, chưa cấp token |
| `POST /api/auth/verification/confirm` | Xác minh email hoặc số điện thoại bằng mã 6 chữ số |
| `POST /api/auth/verification/resend` | Gửi lại mã sau 60 giây; trả 202 kể cả khi email không tồn tại |
| `GET /api/auth/development/code?channel=email&destination=...` | Chỉ trong Development trên loopback; xem mã thử nghiệm khi chưa có nhà cung cấp gửi thật |
| `POST /api/auth/login` | Đăng nhập bằng email hoặc số điện thoại đã xác minh và mật khẩu; khóa 15 phút sau 5 lần sai |
| `POST /api/auth/refresh` | Rotate refresh token và cấp access token mới |
| `POST /api/auth/logout` | Thu hồi refresh token; gọi lặp lại vẫn trả 204 |
| `GET /api/users/me` | Hồ sơ tài khoản hiện tại; yêu cầu Bearer token |
| `POST /api/users/operators` | Admin tạo tài khoản Operator |
| `GET /health/live` | Process API đang phản hồi; không kiểm tra database |
| `GET /health/ready` | Kiểm tra kết nối database; 503 nếu không sẵn sàng |

Swagger UI và OpenAPI chỉ được mở trong **Development**. Khi đăng ký, nhập số di động Việt Nam (`09...` hoặc `+849...`). Lấy mã từ endpoint Development trên loopback, xác minh kênh `email` hoặc `phone`, rồi gọi `POST /api/auth/login` với `identifier` (email hoặc số điện thoại) và `password`. Để gọi endpoint cần đăng nhập, chọn **Authorize** và dán `accessToken` (Swagger tự thêm tiền tố `Bearer`). Không dùng endpoint lấy mã Development trong môi trường thật.

Health endpoints kiểm tra sức khỏe hệ thống backend.

Logout thu hồi refresh token của phiên hiện tại. Access token đã phát hành tiếp tục hợp lệ tối đa 15 phút; mọi request có token vẫn kiểm tra tài khoản còn active và `SecurityStamp` chưa thay đổi. Refresh token có hạn tối đa 30 ngày từ lúc đăng nhập; việc xoay token không gia hạn mốc này. Chưa cấu hình nhà cung cấp email/SMS Production nên đăng ký thật sẽ báo lỗi cấu hình thay vì giả vờ đã gửi mã.

`Page >= 1`, `1 <= PageSize <= 100`. Request tạo offset vượt giới hạn số nguyên bị từ chối bằng HTTP 400. Kết quả phân trang có `items`, `page`, `pageSize`, `totalCount`.

Các response lỗi dùng Problem Details. Middleware thêm header `X-Request-ID`; lỗi có `traceId` để đối chiếu log. Request logging không ghi query string, request/response body hoặc thông tin đăng nhập.

## 8. Dữ liệu demo

Sau khi áp dụng migration, có thể thêm 10 trạm trọng điểm minh họa tại TP.HCM, Hà Nội, Đà Nẵng và Quy Nhơn vào database local:

```powershell
Get-Content -Raw '.\backend\EV Charge & Swap Management System\Data\Seed\dev-locations.sql' | docker exec -i ev-charge-swap-postgres-dev psql -v ON_ERROR_STOP=1 -U ev_user -d ev_charge_swap
```

Để thử bản đồ và tìm trạm theo bán kính trên tập dữ liệu hoàn chỉnh, có thêm seed **100 trạm sạc & đổi pin thực tế** trải dài khắp các tỉnh thành trên toàn quốc (TP.HCM, Hà Nội, Đà Nẵng, Quy Nhơn, Hải Phòng, Quảng Ninh, Bình Dương, Đồng Nai, Cần Thơ, Nha Trang, Vũng Tàu, Đà Lạt...). Tọa độ được đối chiếu chính xác theo các địa danh thực tế (TTTM Vincom, AEON Mall, Lotte Mart, Bến xe, Sân bay, Trạm dừng cao tốc, Cây xăng Petrolimex) hoàn toàn trên đất liền, không rơi ra biển hoặc núi. Phân bổ thực tế gồm 3 loại: vừa sạc vừa đổi pin, chỉ sạc ô tô điện, và chỉ đổi pin xe máy điện:

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

Chín entity lõi dự kiến: `User`, `Location`, `ChargingBay`, `Battery`, `Reservation`, `ChargingSession`, `BatterySwap`, `Invoice`, `Payment`. Hiện đã triển khai `User`, `Location` và entity hạ tầng `RefreshToken`.

1. Thêm chỗ sạc, pin, phân công nhân viên và giờ phục vụ cho các Location đã có.
2. Thêm đặt chỗ 15 phút, chống đặt trùng và job hết hạn.
3. Thêm phiên sạc, giao nhận pin và lịch sử sự cố.
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
