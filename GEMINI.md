# 🛡️ CÁC NGUYÊN TẮC BẢO TRÌ & NÂNG CẤP HỆ THỐNG .NET (MAINTENANCE GUARDRAILS)

Khi làm việc trong dự án này (hoặc khi người dùng yêu cầu liên quan đến bảo trì, nâng cấp, review kiến trúc):

### 1. Tôn chỉ "Zero Breaking Changes" & Bảo vệ Cơ sở Dữ liệu
- **Không tự ý thay đổi quy ước CSDL:** Tuyệt đối KHÔNG đề xuất hoặc tự động cài các package can thiệp vào cách đặt tên bảng/cột như `EFCore.NamingConventions`, trừ khi người dùng yêu cầu rõ ràng. Luôn tôn trọng schema CSDL đã tồn tại để tránh lỗi runtime hoặc mất dữ liệu.
- **Tôn trọng quy ước sẵn có (Convention over Preference):**
  - Nếu dự án đã dùng Data Annotations (`[Required]`, `[Range]`) -> tiếp tục dùng Data Annotations, không tự ý đưa `FluentValidation` vào gây phân mảnh mã nguồn.
  - Nếu dự án đã có Swagger UI -> giữ nguyên, không thay thế bằng các UI mới như `Scalar` nếu không có nhu cầu thực tế.

### 2. Ưu tiên Package an toàn cho vận hành
- Trong môi trường bảo trì, package được ưu tiên số 1 là **`Serilog.AspNetCore`** vì: an toàn 100% với CSDL, không đổi API contract, hỗ trợ ghi log có cấu trúc và truy vết lỗi (`TraceId`) khi điều tra sự cố.

### 3. Phân định rõ ràng khi lập kế hoạch / Đặc tả
- Khi người dùng yêu cầu tạo kế hoạch hoặc tài liệu hướng dẫn (cho Codex, lập trình viên hoặc AI khác):
  - **Tách riêng Phần Template/Hạ tầng** (Solution naming, Docker, Compose, CI/CD, Package references).
  - **Tách riêng Phần Code C#** (Domain, Entities, Business Services, Controllers, Unit/Integration Tests).

### 4. Tuân thủ nghiêm ngặt ranh giới "Không sửa code"
- Khi người dùng chỉ định rõ "Không được sửa code", chỉ thực hiện phân tích, kiểm toán, hoặc tạo tài liệu đặc tả/hướng dẫn trong thư mục brain/artifacts. Tuyệt đối không chỉnh sửa mã nguồn C# trong repo.
