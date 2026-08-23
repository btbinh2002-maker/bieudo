# Phase 1 — Domain Analysis: Biểu đồ chạy tàu khách HN–SG với xử lý tránh/vượt

Trạng thái repo hiện tại: thư mục rỗng, chưa có source code / database nào để tái sử dụng.
Toàn bộ domain model dưới đây là đề xuất mới, chưa có code solver đi kèm (đúng yêu cầu Phase 1).

---

## 0. Cách hiểu bài toán

Đây là bài toán **cyclic single-track train timetabling with meet/overtake conflict resolution**,
thuộc họ bài toán *Train Timetabling Problem (TTP)* trên đường đơn, có các đặc thù:

- Mạng là **một tuyến tuyến tính** (line graph), không phải mạng tổng quát → đơn giản hoá đáng kể so với
  TTP trên mạng lưới (không cần routing, chỉ cần scheduling theo thứ tự ga cố định).
- **Giờ đi ga đầu và giờ đến ga cuối là hard constraint cố định** cho từng tàu → bài toán thực chất là
  "làm khớp một quỹ đạo có 2 đầu mút cố định vào giữa các quỹ đạo khác mà không xung đột", với
  **quỹ thời gian dư (buffer)** là biến số duy nhất có thể phân phối lại dọc hành trình.
- Có 2 loại xung đột cơ bản trên đường đơn: **MEET** (ngược chiều tranh chấp khu gian) và
  **OVERTAKE/HEADWAY** (cùng chiều, tàu nhanh đuổi kịp tàu chậm hoặc vi phạm giãn cách).
- Bài toán có tính **chu kỳ 24h** — nghiệm phải là một *steady-state pattern* tự nhất quán khi lặp vô hạn,
  **không phải** một lịch chỉ đúng cho một ngày cụ thể.
- Ràng buộc **không được greedy cận thị**: một quyết định tránh tại ga X có thể tiêu hết buffer cần thiết
  cho một xung đột khác phía sau → cần forward-looking search (rolling horizon + beam search), tương tự
  cách tiếp cận trong các paper TTP dùng branch & bound / MILP nhưng ở đây dùng heuristic search có kiểm
  soát độ rộng (beam) để giữ tính khả thi về hiệu năng với ~178 ga × nhiều tàu/ngày.

**Điểm mấu chốt cần lưu ý ngay từ đầu (khác với cách đọc "chu kỳ 24h" thông thường):**
Hành trình Hà Nội – Sài Gòn của một đoàn tàu khách thực tế kéo dài **~30–34 giờ**, tức là **dài hơn một
chu kỳ 24h**. Điều này có hệ quả quan trọng:

1. `JourneyTime`, `MinimumJourneyTime`, `TotalBuffer` của một tàu được tính bằng **số phút tuyệt đối**,
   không lấy modulo 24h — một tàu hoàn toàn có thể có `JourneyTime` = 1900 phút.
2. Tính chu kỳ 24h không có nghĩa "mỗi tàu chạy xong trong 1 ngày" — nó có nghĩa là **lịch chạy tàu tại
   mỗi ga lặp lại mỗi 24h** (cùng giờ tàu SE1 đi qua ga X mỗi ngày). Một đoàn tàu cụ thể tại một thời điểm
   là một "instance" của một *train pattern*, và pattern đó sinh ra vô số instance tại
   `t0 + 24h·k, k ∈ ℤ`.
3. Do đó cửa sổ nhân bản "Day-1/Day0/Day+1" (≈ −24h → +48h) mà đề bài gợi ý ở mục 21 **chưa đủ** nếu
   hành trình dài hơn 24h — xem chi tiết ở mục 15 bên dưới. Đây là một chỉnh sửa quan trọng so với đặc tả gốc.

> **Cập nhật (sau phản hồi):** mục 0 và mục 11/15 dưới đây đã được viết lại để phát biểu bài toán đúng
> như một **cyclic timetabling problem thực sự** (tìm 1 pattern chu kỳ 1440 phút, không phải lập lịch
> nhiều ngày độc lập). Xem mục 0.1 và mục 11 (đã viết lại toàn bộ).

### 0.1 Phát biểu lại bài toán: TrainService (pattern) vs. TrainInstance (bản sao theo chu kỳ)

Đây là điểm chỉnh sửa quan trọng nhất so với bản Phase 1 đầu tiên, để tránh hiểu nhầm "lập lịch cho vài
ngày rồi cắt lấy 1 ngày đại diện".

**Định nghĩa hình thức:** Với mỗi tàu khách, ta định nghĩa một **`TrainService`** — pattern chu kỳ
`P = 1440` phút, biểu diễn bằng lịch trình canonical tại chu kỳ 0:

```text
Schedule(Service, Station, CycleIndex)
    = Schedule(Service, Station, 0) + CycleIndex × 1440,     CycleIndex ∈ ℤ
```

`TrainInstance(ServiceId, CycleIndex)` **không phải một entity lưu trữ độc lập** — nó là một **view suy
diễn** (derived, read-only) từ `TrainService` bằng phép dịch thời gian `+ CycleIndex × 1440`. Không bao
giờ được tạo, lưu, hay chỉnh sửa một `TrainInstance` như một tàu độc lập.

**Phát biểu toán học của bài toán:**

> Tìm một hàm lịch trình canonical `Schedule(Service, Station, 0)` cho mọi `TrainService`, sao cho khi
> hàm này được mở rộng tuần hoàn theo chu kỳ `P = 1440` phút trên toàn trục thời gian (`ℤ`), **mọi** ràng
> buộc cứng (running time tối thiểu, dừng bắt buộc, tăng/giảm tốc, no-conflict giữa các
> `TrainInstance` bất kỳ ở bất kỳ `CycleIndex` nào, headway, năng lực tránh/vượt của ga, giờ đi/đến cố
> định tại chu kỳ 0) đều được thỏa mãn, và tổng cost (mục 10) trên chu kỳ 0 là nhỏ nhất.

Vì hàm cost và constraint đều bất biến theo phép dịch `+1440` (do định nghĩa `Schedule` ở trên), tối ưu
trên chu kỳ 0 tương đương tối ưu trên toàn bộ trục thời gian vô hạn — đây là lý do bài toán vô hạn chu kỳ
có thể quy về **một bài toán tối ưu hữu hạn biến** (biến quyết định chỉ thuộc chu kỳ 0).

**Hệ quả bắt buộc cho kiến trúc:**

1. **Decision variable (biến mà solver được phép thay đổi) chỉ tồn tại trên `TrainService` (chu kỳ 0)**,
   không bao giờ trên một `TrainInstance` riêng lẻ.
2. Khi solver quyết định dịch chuyển lịch trình của `TrainService` X (vd. thêm chờ tránh tại ga k), thay
   đổi đó áp dụng cho `Schedule(X, ·, 0)` — và do định nghĩa suy diễn ở trên, **mọi** `TrainInstance` của
   X (ở mọi `CycleIndex`) tự động nhận đúng cùng thay đổi tương đối, dịch đúng `1440 × CycleIndex`. Solver
   không bao giờ "sửa SE1/-1 khác với SE1/0" — điều đó không thể xảy ra vì instance không lưu trạng thái
   riêng.
3. **Hệ quả quan trọng cần lưu ý khi resolve conflict:** một xung đột phát hiện được giữa
   `Instance(A, 0)` và `Instance(B, d)` (d ≠ 0) được sửa bằng cách dịch chuyển `TrainService B`. Nhưng vì
   thay đổi đó áp dụng cho **toàn bộ** các instance của B, nó có thể đồng thời làm phát sinh xung đột MỚI
   giữa `Instance(A, 0)` và một instance khác của B (vd. `Instance(B, d±1)`), hoặc giữa B và các
   `TrainService` khác ở các `CycleIndex` khác. Vì vậy bước "recalculate affected" sau mỗi candidate
   **không được chỉ re-check đúng cặp instance đã gây ra conflict ban đầu** — phải re-scan toàn bộ cửa sổ
   chu kỳ `[-K, K]` cho riêng `TrainService` vừa bị sửa (chi tiết ở mục 9.2 và mục 11).

---

## 1. Domain Model đề xuất

```text
Domain
 ├─ Station
 ├─ StationTrack            (mới — chi tiết hoá năng lực tránh/vượt)
 ├─ Section
 ├─ Direction (enum)
 ├─ TrainService             (canonical pattern, chu kỳ 0 — CHỖ DUY NHẤT chứa decision variable)
 ├─ TrainInstance            (KHÔNG lưu trữ — view suy diễn TrainService + CycleIndex×1440)
 ├─ TrainStopRequirement
 ├─ TrainServiceTrajectory   (kết quả: danh sách TimetableEntry của TrainService tại chu kỳ 0)
 ├─ TimetableEntry
 ├─ SectionOccupation        (sinh cho cả TrainInstance khi detect conflict xuyên chu kỳ)
 └─ Conflict (MEET | HEADWAY | OVERTAKE)

Configuration
 ├─ StopRules          (PassengerStop, TechnicalStop, CombineRule)
 ├─ RunningTimeRules   (AccelerationPenalty, DecelerationPenalty)
 ├─ HeadwayRules       (SectionReleaseHeadwayMinutes — dùng chung MEET/HEADWAY, mục 5.6; OvertakeHeadway riêng)
 ├─ SolverParameters   (CandidateWindow, BeamWidth, LookAheadConflicts, CyclicRadius...)
 └─ CostWeights        (alpha..epsilon, objective weights)

Engine  (Phase 2+, chưa code)
 ├─ MinimumTimetableBuilder
 ├─ BufferCalculator / BufferAllocator
 ├─ ConflictDetector / ConflictAnalyzer
 ├─ CandidateGenerator
 ├─ RequiredShiftCalculator
 ├─ CandidateEvaluator
 ├─ MeetResolver / OvertakeResolver
 ├─ BeamSearchSolver
 ├─ TimetableOptimizer
 └─ TimetableValidator
```

### 1.1 Station

```text
Station
  StationId
  Code
  Name
  Sequence            // thứ tự 1..178 trên tuyến HN→SG
  Tracks: List<StationTrack>
```

```text
StationTrack
  TrackId
  StationId
  TrackType           // MainThrough | Siding | Platform
  UsableForMeet: bool
  UsableForOvertake: bool
  MaxTrainLength?      // để dành cho sau, không dùng ở Phase 1-7
```

Từ danh sách `Tracks`, suy ra:

```text
Station.CanMeet      = số track UsableForMeet     >= 2
Station.CanOvertake  = số track UsableForOvertake >= 2   (thường yêu cầu chặt hơn CanMeet:
                        cần 1 track cho tàu nhanh chạy thẳng + 1 track/siding cho tàu chậm đỗ chờ)
Station.MaxSimultaneousTrains = số track khả dụng cùng lúc
```

Lý do tách `StationTrack` thay vì chỉ 2 cờ `CanMeet/CanOvertake` trên `Station`: đề bài yêu cầu
"khai báo số đường trong ga và khả năng tránh/vượt" — việc mô hình theo track cho phép sau này mở rộng
sang: gán track cụ thể cho từng tàu, giới hạn số tàu đỗ đồng thời (>2 tàu cùng lúc tại ga lớn), chiều dài
đoàn tàu vs. chiều dài đường, v.v. mà không phải đổi lại model gốc.

### 1.2 Section (khu gian)

```text
Section
  SectionId
  FromStationSequence      // i
  ToStationSequence        // i+1
  MinRunningTime: Map<Direction, int minutes>
  NumberOfTracks: int      // mặc định 1 (đường đơn) — để ngỏ cho double-track sau này
  Passable: bool
```

`NumberOfTracks > 1` là điểm mở để sau này Section tự cho phép 2 tàu ngược chiều cùng lúc (double track)
mà không cần đổi kiến trúc ConflictDetector — chỉ cần điều kiện MEET-conflict thêm
`Section.NumberOfTracks < 2`.

### 1.3 Direction

```text
enum Direction { Inbound (HN→SG, số chẵn), Outbound (SG→HN, số lẻ) }
```

### 1.4 TrainService (canonical pattern) & TrainInstance (view suy diễn)

```text
TrainService
  ServiceId
  TrainCode
  Direction
  OriginStationSeq
  DestinationStationSeq
  FixedDepartureTimeOfDay: int minutes   // ∈ [0, 1440) — giờ xuất phát TRONG NGÀY, đây là canonical
  JourneyTime: int minutes               // = FixedArrivalTime - FixedDepartureTime, có thể > 1440
  Priority: int
  StopRequirements: List<TrainStopRequirement>
```

Lưu ý so với bản trước: **`FixedDepartureTimeOfDay` được chuẩn hoá về `[0, 1440)`** — đây chính là "chu
kỳ 0" theo định nghĩa toán học ở mục 0.1 (chọn instance đại diện là instance có giờ xuất phát ga đầu nằm
trong `[0,1440)`). `FixedArrivalTime` không lưu như một mốc tuyệt đối riêng — nó luôn được tính
`= FixedDepartureTimeOfDay + JourneyTime`, và hoàn toàn có thể vượt quá 1440 (vd. 2260 phút, tức 13:40
ngày hôm sau) vì đây vẫn là cùng một instance chu kỳ 0, chỉ là hành trình vật lý kéo dài qua nhiều ngày.

```text
TrainStopRequirement
  StationSeq
  RequiresPassengerStop: bool
  RequiresTechnicalStop: bool
  StopDurationOverride?: int   // nếu ga này có quy định thời gian dừng riêng khác default
```

`TrainService.Route` (danh sách ga đi qua) không lưu tường minh — suy ra trực tiếp từ
`[OriginStationSeq .. DestinationStationSeq]` theo `Direction` (đây là tuyến tuyến tính nên route luôn
là một đoạn liên tục các ga theo thứ tự `Sequence`). Ga nào không có trong `StopRequirements` → tàu
chạy thông qua (default, đúng mục 3 của đề bài). `StopRequirements` áp dụng **giống hệt cho mọi
instance** — pattern lặp lại mỗi ngày như nhau, đúng giả định steady-state của bài toán.

```text
TrainInstance   // KHÔNG phải class lưu trữ — chỉ là hàm/struct tính toán tại chỗ (computed view)
  ServiceId
  CycleIndex: int                                  // n ∈ ℤ
  Departure(StationSeq)  = TrainService.Schedule(StationSeq).Departure + CycleIndex × 1440
  Arrival(StationSeq)    = TrainService.Schedule(StationSeq).Arrival   + CycleIndex × 1440
```

`CycleIndex = 0` tương ứng đúng `TrainServiceTrajectory` (mục 1.5) không dịch chuyển. Toàn bộ
`ConflictDetector` (mục 5, 11) làm việc trên `TrainInstance` (vì conflict là hiện tượng vật lý xảy ra ở
một thời điểm tuyệt đối cụ thể trên trục thời gian vô hạn), nhưng **toàn bộ `Engine` giải xung đột
(RequiredShiftCalculator, Resolver, BeamSearchSolver) chỉ được phép ghi thay đổi vào `TrainService`**
(mục 0.1, hệ quả 1–2). Đây là ranh giới kiến trúc quan trọng: *đọc* qua `TrainInstance`, *ghi* qua
`TrainService`.

### 1.5 TrainServiceTrajectory & TimetableEntry

```text
TimetableEntry
  StationSeq
  ArrivalTime:  int?           // null nếu là ga xuất phát
  DepartureTime: int?          // null nếu là ga đến cuối
  StopType: enum { Through, Passenger, Technical, PassengerAndTechnical, ForcedMeet, ForcedOvertake }
  StopDuration: int
  RunningTimeFromPrev: int
  AccelerationApplied: bool
  DecelerationApplied: bool
  RecoveryTimeFromPrev: int     // buffer/recovery cấy vào khu gian trước ga này
  CumulativeInsertedDelayMinutes: int
                                 // tổng số phút delay đã TỪNG được chèn (InsertDelay) tính dồn tới ga
                                 // này - KHÔNG phải lượng buffer/recovery thực tế đã tiêu (phần đó có
                                 // thể đã được RecoveryTimeFromPrev hấp thụ). Dùng để audit "đã có bao
                                 // nhiêu quyết định delay áp lên tàu này"; muốn biết buffer còn lại thì
                                 // dùng ForwardSlack(T,k) (mục 4.1), không suy diễn từ trường này.
```

`TrainServiceTrajectory = List<TimetableEntry>` theo đúng thứ tự ga trên route, thuộc về `TrainService`
tại chu kỳ 0 — đây là **nơi duy nhất solver ghi trạng thái**. Đây chính là "đường chạy" dùng để vẽ biểu
đồ thời gian – không gian (khi vẽ nhiều ngày liên tiếp, chỉ cần dịch `+1440×n` tại thời điểm render, không
lưu thêm bản sao nào).

### 1.6 SectionOccupation (dẫn xuất từ TrainInstance, dùng cho conflict detection)

```text
SectionOccupation
  SectionId
  ServiceId
  CycleIndex          // instance nào sinh ra occupation này (0, ±1, ±2, ... theo cửa sổ K, mục 11)
  Direction
  EntryTime   = TrainInstance(ServiceId, CycleIndex).Departure(ga đầu khu gian)
  ExitTime    = TrainInstance(ServiceId, CycleIndex).Arrival(ga cuối khu gian)
```

Occupation luôn được sinh **theo instance** (có `CycleIndex`) chứ không theo service trực tiếp, vì bản
thân hiện tượng chiếm dụng khu gian là một sự kiện vật lý tại một thời điểm tuyệt đối cụ thể — một
`TrainService` chạy hàng ngày sẽ sinh ra nhiều `SectionOccupation` cho cùng một `SectionId` (một cho mỗi
`CycleIndex` còn nằm trong cửa sổ quan sát).

### 1.7 Conflict

```text
Conflict
  ConflictId
  Type: enum { MEET, HEADWAY, OVERTAKE }
  ConstraintKind: enum { SectionOverlap, SectionReleaseHeadway, OrderReversal }   // mục 5.2/5.4
  ServiceA, CycleIndexA
  ServiceB, CycleIndexB
  SectionId (hoặc dải Section liên tiếp cho OVERTAKE)
  ConflictStartTime, ConflictEndTime      // thay cho ConflictTimeWindow — cùng nội dung, tách 2 field
                                           // cho dễ dùng trực tiếp trong so sánh/sort mà không destructure

  RequiredHeadwayMinutes: int             // = SectionReleaseHeadwayMinutes tại thời điểm detect (mục 5.6)
  ActualGapMinutes: int                   // = Later.EntryTime - Earlier.ExitTime, CÓ THỂ ÂM (mục 5.2)
  HeadwayDeficitMinutes: int              // = max(0, RequiredHeadwayMinutes - ActualGapMinutes)

  Severity / Difficulty   // xem mục 12
```

> **Sửa lại (2026-08-23, chốt trước Phase 3):** thêm `ConstraintKind` và 3 field số
> (`RequiredHeadwayMinutes`/`ActualGapMinutes`/`HeadwayDeficitMinutes`) để `Conflict` tự mang đủ dữ liệu
> phân biệt "vi phạm gap thuần tuý" khỏi "overlap vật lý thật" và "đảo thứ tự nhiều Section" — không cần
> `RequiredShiftCalculator`/`CandidateEvaluator` tính lại từ đầu (mục 5.5 đã tính sẵn khi detect).

Một `Conflict` luôn tham chiếu tới **cặp instance cụ thể** (`Service + CycleIndex` cho mỗi bên) vì đó là
thứ va chạm nhau trên trục thời gian thực; nhưng khi resolve, quyết định sửa luôn ghi ngược lại
`TrainService` (bỏ `CycleIndex`, quy đổi thời điểm xung đột về chu kỳ 0) — xem mục 0.1 hệ quả 2–3 và mục
9.2.

Thiết kế 3 loại conflict dùng **chung 1 hạ tầng phát hiện** — điều kiện Section Release Headway trên
occupation (mục 5.2), không phải "interval overlap" đơn thuần như bản nháp trước — nhưng khác điều kiện
kích hoạt cụ thể (chiều + `OrderReversal` mục 5.3/5.4) và khác Resolver, đúng yêu cầu "không xây kiến
trúc chỉ dùng riêng cho MEET" (mục 14 đề bài).

---

## 2. Biểu diễn thời gian (mục 22)

Toàn bộ tính toán dùng **integer minutes**, tuyệt đối so với mốc `Day0 00:00 = 0`, có thể âm hoặc > 1440
(và trong thực tế **vượt xa** 1440 vì hành trình HN–SG dài hơn 1 ngày — xem mục 0). Không dùng `DateTime`
trong lõi thuật toán; chỉ convert sang giờ đồng hồ ở lớp presentation/input-output.

Đề xuất bọc trong một kiểu nhỏ `TimeMinutes` (type alias / value object), không dùng `int` trần, để:
- tránh nhầm với các đại lượng `int` khác (station sequence, priority...);
- dễ đổi đơn vị sang giây sau này nếu cần độ chính xác cao hơn (đường sắt VN hiện tại làm tròn phút nên
  phút là đủ, nhưng nên để ngỏ).

---

## 3. Công thức TotalBuffer (mục 7)

```text
JourneyTime(T)         = FixedArrivalTime(T) - FixedDepartureTime(T)         // số tuyệt đối, không mod 24h

MinimumJourneyTime(T)  = Σ MinRunningTime(section, Direction)
                        + Σ MandatoryStopTime(station)      // theo StopRules (mục 4)
                        + Σ AccelerationPenalty (mỗi lần train khởi hành sau khi đã dừng)
                        + Σ DecelerationPenalty (mỗi lần train sẽ dừng ở cuối khu gian)

TotalBuffer(T)         = JourneyTime(T) - MinimumJourneyTime(T)
```

Ràng buộc cứng: `TotalBuffer(T) >= 0` cho mọi tàu — nếu vi phạm, input timetable (giờ đi/đến cố định)
tự thân đã infeasible, phải báo lỗi **trước khi** chạy conflict resolution (fail fast ở
`MinimumTimetableBuilder`, không để lọt xuống solver).

### 3.1 Quy tắc dừng kết hợp (mục 4)

```text
StopRule (configurable, per-station hoặc global default):
  enum CombineMode { Max, Sum, Custom }

  Max    → StopTime = MAX(PassengerStop, TechnicalStop)      // default
  Sum    → StopTime = PassengerStop + TechnicalStop
  Custom → hàm do người dùng khai báo (vd: tác nghiệp kỹ thuật đã bao hàm đón/trả khách nếu >= X phút)
```

### 3.2 Running time (mục 5)

```text
RunningTime(i → i+1, Train T)
  = MinRunningTime(section, Direction)
  + AccelerationPenalty   nếu Departure(T, i) xảy ra sau một lần dừng tại i (không phải through)
  + DecelerationPenalty   nếu Arrival(T, i+1) sẽ kèm theo dừng tại i+1
  + RecoveryTimeFromPrev  (phần buffer được cấy vào khu gian này — xem mục 4 BufferAllocator)
```

Toàn bộ 3 hằng số (`PassengerStop=3`, `TechnicalStop=20`, `AccelerationPenalty=2`,
`DecelerationPenalty=1`, `SectionReleaseHeadwayMinutes=3` — mục 5.6) nằm trong `Configuration`, **không
hard-code** trong Engine —
Engine chỉ nhận `IStopRules`, `IRunningTimeRules`, `IHeadwayRules` qua constructor/tham số.

---

## 4. BufferAllocator & UsableSlack (mục 8, câu hỏi 10) — **đã viết lại theo phản hồi**

Bản đầu tiên gộp "usable slack sau xung đột" thành một công thức duy nhất. Cần tách rõ **3** khái niệm
khác nhau về bản chất, vì chúng có nguồn gốc và điều kiện sử dụng khác nhau:

```text
TotalBuffer(T)         = hằng số theo mục 3, cố định khi timetable đầu vào cố định (mục 15.10:
                          bất biến qua mọi TrajectoryPropagator.InsertDelay, không chỉ tại t=0).
AllocatedRecovery(T)   = Σ RecoveryTimeFromPrev CÒN LẠI dọc trajectory hiện tại — buffer đã được "đặt
                          chỗ" vào một section cụ thể nhưng CHƯA bị tiêu, vẫn khả dụng nguyên vẹn.
ConsumedBuffer(T)      = tổng phút buffer đã THỰC SỰ bị một quyết định delay/forced-stop/waiting sử
                          dụng (= TimetableEntry.CumulativeInsertedDelayMinutes tại destination) —
                          KHÔNG bao gồm AllocatedRecovery (recovery mới "đặt chỗ" nhưng chưa tiêu).
UnallocatedBuffer(T)   = TotalBuffer(T) - AllocatedRecovery(T) - ConsumedBuffer(T)   // phần chưa hề
                          được quyết định gán vào đâu cả — vẫn "tự do" hoàn toàn.

RemainingBuffer(T)     = TotalBuffer(T) - ConsumedBuffer(T)
                        = AllocatedRecovery(T) + UnallocatedBuffer(T)   // TƯƠNG ĐƯƠNG — chỉ trừ phần
                          ĐÃ TIÊU, KHÔNG trừ phần đã đặt chỗ nhưng còn khả dụng. Con số TOÀN CỤC — KHÔNG
                          BAO GIỜ dùng trực tiếp để quyết định 1 conflict cụ thể (mục 4.3).
```

> **Sửa lại (2026-08-23, thống nhất với `BufferState`/`BufferCalculator.ComputeBufferState`, đã code &
> test — `src/TrainTimetable.Engine/BufferCalculator.cs`):** bản nháp trước gộp nhầm
> `RecoveryTimeFromPrev đã cấy` (= `AllocatedRecovery`, còn khả dụng) vào chung `UsedBuffer` với
> `WaitingTime` (= `ConsumedBuffer`, đã tiêu thật) — hai đại lượng khác bản chất, không được cộng chung.
> Ví dụ: `TotalBuffer=20, AllocatedRecovery=8` (đã cấy vào một section nhưng chưa ai đụng tới),
> `ConsumedBuffer=0` (chưa quyết định delay nào tiêu tới nó) → `UnallocatedBuffer=12`, nhưng
> `RemainingBuffer` vẫn phải là **20** (cả 8 phút đã cấy lẫn 12 phút chưa cấy đều còn "sống" — chỉ khi
> một `InsertDelay` thực sự tiêu vào 8 phút đó thì nó mới chuyển thành `ConsumedBuffer` và
> `RemainingBuffer` mới giảm). Nếu muốn giữ tên `UsedBuffer`, định nghĩa lại `UsedBuffer := ConsumedBuffer`
> (không cộng `AllocatedRecovery`) — tài liệu từ đây dùng `ConsumedBuffer` làm tên chính thức.

### 4.1 ForwardSlack(T, k) — "usable slack sau xung đột", trần cứng, luôn an toàn để dùng ngay

```text
ForwardSlack(T, k) = FixedArrivalTime(T)
                    - CurrentDeparture(T, k)
                    - MinimumRemainingJourneyTime(T, k → destination)
```

Đây là **maximum forward delay mà hành trình có thể hấp thụ TỪ ga k trở đi**: tổng lượng thời gian bổ
sung tối đa có thể tiêu kể từ vị trí k trở đi mà tàu vẫn còn khả năng về đúng `FixedArrivalTime`, với giả
định phần còn lại (từ k đến đích) chạy đúng bằng tối thiểu (không còn recovery nào để dùng thêm).

> **Sửa lại (2026-08-23, review trước Phase 3):** bản nháp trước mô tả sai rằng công thức này "loại trừ
> buffer nằm ở tương lai xa". **Sai** — `ForwardSlack(T,k)` **BAO GỒM toàn bộ** slack/recovery có thể
> khai thác được ở phía sau k, **bất kể nằm gần hay xa** k (miễn còn nằm giữa k và đích): nếu tàu có 40
> phút buffer và cả 40 phút đó đều nằm sau k, `ForwardSlack(T,k) = 40`, không phải một con số nhỏ hơn.
> Thứ **duy nhất** `ForwardSlack(T,k)` loại trừ là phần slack/recovery đã nằm **TRƯỚC** k — tức
> `RedistributableSlack(T,k)` (mục 4.2) — vì phần đó đã "chốt" vào `CurrentDeparture(T,k)` (đã cộng vào
> thời điểm rời k), không thể tiêu thêm ở k mà không revalidate ngược. Cách nhớ đúng:
> `ForwardSlack(T,k)` = "toàn bộ dư địa còn lại kể từ đúng thời điểm hiện tại đang đứng ở k", không phải
> "chỉ phần dư địa nằm ngay sát k".

Chứng minh công thức tương đương làm rõ đúng chỗ này (mục 15.10, `BufferCalculator.ComputeBufferState` —
đã implement & test): gọi `TotalRecoveryAfterK` = tổng `RecoveryTimeFromPrev` của mọi ga SAU k trên
trajectory hiện tại, và `UnallocatedBuffer` = phần `TotalBuffer` chưa hề được cấy thành recovery ở bất kỳ
đâu (mục 15.10). Khai triển đại số từ định nghĩa `ForwardSlack` cho ra:

```text
ForwardSlack(T, k) = UnallocatedBuffer + TotalRecoveryAfterK
```

Đúng nghĩa đen: **toàn bộ** phần chưa phân bổ (nằm "ảo" ở cuối hành trình) **cộng với toàn bộ** recovery
đã cấy sẵn ở BẤT KỲ ga nào sau k (gần hay xa k đều tính, vì `TotalRecoveryAfterK` là một tổng không phân
biệt vị trí) — không có khái niệm "quá xa nên không tính".

Vì tính an toàn (không phụ thuộc giả định về việc tái phân bổ ngược), **`RequiredShiftCalculator` mặc
định chỉ được phép tiêu tới `ForwardSlack(T,k)`** cho một xung đột tại k — đây là con số dùng trong mọi
kiểm tra feasibility ở mục 7, 8. Mô hình hiện tại **không có ràng buộc giờ đến/đi cố định tại các ga
trung gian** (chỉ có ở ga xuất phát và ga cuối — mục "Dữ liệu của tàu khách"), nên `ForwardSlack(T,k)` là
đúng nghĩa **hard upper bound** cho tổng delay có thể chèn tại k mà không cần xét thêm ràng buộc trung
gian nào khác; nếu sau này có thêm giờ cố định tại một ga trung gian (vd. ga kết nối liên vận), công thức
phải thu hẹp lại theo mốc cố định gần nhất phía sau k thay vì `FixedArrivalTime` tại đích.

### 4.2 RedistributableSlack(T, k) — buffer đã cấy Ở PHÍA TRƯỚC k, có thể "mượn" nhưng phải re-validate

```text
RedistributableSlack(T, k) = Σ RecoveryTimeFromPrev(m)  với mọi ga m mà StationSequence(m) <= k
                              // LƯU Ý biên: bao gồm CẢ RecoveryTimeFromPrev(k) chính nó — tức recovery
                              // của khu gian dẫn VÀO k. ForwardSlack(T,k) (mục 4.1) chỉ cộng recovery
                              // của các ga SAU k (StationSequence(m) > k, đúng vòng lặp trong
                              // BufferCalculator.ComputeForwardSlackMinutes bắt đầu từ index+1) - nên
                              // biên đúng để 2 con số cộng lại vừa khít AllocatedRecovery (không thiếu,
                              // không đè) phải là "<= k" ở đây, không phải "< k".
```

Đây là phần recovery-time **đã hoạch định** ở các khu gian trước k (tính cả khu gian dẫn vào k). Về nguyên tắc có thể loại bỏ (không
cấy recovery ở đó nữa) để tàu đến các ga trước k — và do đó đến cả k — **sớm hơn**, gián tiếp "tạo thêm"
`ForwardSlack(T,k)` (vì `ForwardSlack` giảm khi `CurrentDeparture(T,k)` giảm, theo công thức 4.1 dấu trừ
đứng trước nó nghĩa là làm nó tăng). **Đây không phải "tiền free"**: rút recovery ở ga m khiến tàu đến các
ga sau m (kể cả trước k) sớm hơn lịch cũ, và bản thân sự sớm hơn đó **phải được re-validate** bằng
`ConflictDetector` cho các section giữa m và k — vì rất có thể lịch cũ (đến muộn hơn) chính là để tránh
một xung đột khác đã được giải trước đó ở đoạn này. Vì lý do này, `RedistributableSlack` **không được
Phase 5/6 (`RequiredShiftCalculator` cơ bản) sử dụng ngầm định** — nó chỉ được một cơ chế tường minh, có
gắn nhãn riêng (tạm gọi `SlackReallocationStrategy`, thuộc `BufferAllocator`, đưa vào ở Phase 8 khi đã có
một nghiệm khả thi để cải thiện thêm) sử dụng, với điều kiện bắt buộc: sau khi rút, phải chạy lại
`ConflictDetector` cho toàn bộ đoạn `[m, k]` bị ảnh hưởng và xác nhận không phát sinh xung đột mới trước
khi chấp nhận.

### 4.3 UsableSlackAtConflict(T, k) — con số thực tế `RequiredShiftCalculator` dùng để so sánh

```text
UsableSlackAtConflict(T, k) = ForwardSlack(T, k)                                  // mặc định (Phase 5–7)
                             [+ phần RedistributableSlack(T,k) ĐÃ ĐƯỢC RE-VALIDATE // chỉ khi Phase 8 chủ
                                nếu SlackReallocationStrategy được kích hoạt]      //  động kích hoạt
```

Tóm lại: **"tàu còn nhiều `RemainingBuffer` không đồng nghĩa toàn bộ có thể dùng cho xung đột hiện tại"**
— con số đúng để so sánh với `RequiredShift` luôn là `ForwardSlack(T,k)` (an toàn, cục bộ, tính được ngay
từ trạng thái hiện tại), không phải `RemainingBuffer(T)` (toàn cục). Quan hệ chính xác giữa hai con số
(hệ quả trực tiếp của công thức mục 4.1 vừa chứng minh):

```text
RemainingBuffer(T) = ForwardSlack(T, k) + RedistributableSlack(T, k)      // đúng với MỌI k
```

Suy trực tiếp từ định nghĩa (không phải trùng hợp): `RedistributableSlack(T,k) = RecoveryAtOrBeforeK`
(mục 4.2 — **bao gồm cả** `RecoveryTimeFromPrev(k)`, tức mọi ga `m` với `StationSequence(m) <= k`, đúng
biên đã chốt ở mục 4.2) và `ForwardSlack(T,k) = UnallocatedBuffer + RecoveryAfterK` (mục 4.1, mọi ga
`StationSequence(m) > k`). Vì mọi recovery trên trajectory đều nằm ở đúng một trong hai vùng này (không
chồng lấp, không thiếu chỗ nào — biên `<= k` / `> k` chia trajectory làm đúng 2 phần):
`RecoveryAtOrBeforeK + RecoveryAfterK = AllocatedRecovery` (tổng toàn trajectory, đầu mục 4). Do đó:

```text
ForwardSlack(T,k) + RedistributableSlack(T,k)
    = UnallocatedBuffer + RecoveryAfterK + RecoveryAtOrBeforeK
    = UnallocatedBuffer + AllocatedRecovery
    = RemainingBuffer(T)                                    // đúng theo định nghĩa đầu mục 4
```

`ForwardSlack(T,k)` = phần dùng được ngay, không cần revalidate (mọi slack từ k trở đi, gần hay xa như
đã sửa ở mục 4.1). `RedistributableSlack(T,k)` = phần đã "kẹt" trước k, chỉ dùng được qua cơ chế
reallocation có kiểm chứng riêng (mục 4.2), không mặc định. Không có phần thứ ba nào khác — không tồn
tại khái niệm buffer "sau k nhưng vẫn không tính vào `ForwardSlack`".

`BufferAllocator` (Engine) chịu trách nhiệm: (a) tạo phân bổ recovery-time ban đầu hợp lý dọc hành trình
(vd. rải đều, hoặc ưu tiên rải trước các ga hay xảy ra giao cắt — dùng lịch sử/heuristic), và (b) thực
hiện `SlackReallocationStrategy` (mục 4.2) sau khi đã có nghiệm khả thi, để cải thiện độ đều của recovery
(Objective 6, mục 20) hoặc để "giải cứu" một nhánh beam search suýt infeasible vì thiếu `ForwardSlack`
cục bộ.

> **Làm rõ (sau Phase 2 + Phase 2.5, mục 15.10):** cả (a) và (b) đều thuộc **Phase 8**, không phải
> "Phase 2 khởi tạo" như phát biểu ở bản nháp đầu. `MinimumTimetableBuilder` đã implement (Phase 2, đã
> commit) chỉ tạo trajectory tối thiểu với `RecoveryTimeFromPrevMinutes = 0` khắp nơi — đây **là** trạng
> thái khởi tạo đúng (vì dữ liệu hành trình thật không cho biết buffer nằm ở đâu — mục 15.6), không phải
> giá trị tạm chờ (a) chạy tiếp. (a)/(b) là cải tiến chất lượng, không phải điều kiện cần cho tính đúng
> đắn — xem lập luận đầy đủ ở mục 15.10.

---

## 5. Section Occupation & Conflict Detection (mục 10, 14 — câu hỏi 5,6,7)

> **Sửa lại (2026-08-23, chốt trước khi code Phase 3):** bản nháp trước tách MEET (ngược chiều, chỉ xét
> overlap khoảng thời gian) và HEADWAY (cùng chiều, chỉ xét "giãn cách entry-entry hoặc exit-exit, tuỳ
> cấu hình") như hai rule có **bản chất khác nhau**. Sai theo đúng nghiệp vụ thực tế: quy tắc gốc là
> **"Section Release Headway"** — sau khi một tàu ra khỏi khu gian, phải chờ tối thiểu 3 phút thì tàu
> tiếp theo (BẤT KỂ cùng chiều hay ngược chiều) mới được phép vào khu gian đó. MEET và HEADWAY dùng
> **chung một điều kiện gap** (`Later.EntryTime − Earlier.ExitTime >= SectionReleaseHeadwayMinutes`),
> chỉ khác nhau ở: MEET còn phải xét thêm khả năng **overlap vật lý thật sự** (hai tàu ngược chiều tranh
> chấp khoảng thời gian), còn HEADWAY (cùng chiều) không có khả năng "overlap kiểu MEET" nhưng CÓ khả
> năng suy biến thành OVERTAKE nếu gap âm đủ sâu (mục 5.4). Toàn bộ mục 5 viết lại theo đúng quy tắc này.

### 5.1 Section occupation

Sinh từ `TrainInstance` (mục 1.4/1.6), không trực tiếp từ `TrainService`: với mỗi `ServiceId` và mỗi
`CycleIndex` còn nằm trong cửa sổ chu kỳ `[-K, K]` (mục 11), mỗi cặp `(TimetableEntry[i], TimetableEntry[i+1])`
của `TrainServiceTrajectory` (dịch `+CycleIndex×1440`) sinh
1 `SectionOccupation { SectionId, ServiceId, CycleIndex, Direction, EntryTime, ExitTime }`. Nói cách khác,
`ConflictDetector` **luôn chạy ở "chế độ cyclic"** ngay từ Phase 3 — không có một phiên bản "chỉ trong
ngày" tách riêng rồi mở rộng sau; xem mục 11 để biết cách giới hạn `K` sao cho vẫn hiệu quả.

### 5.2 Section Release Headway — điều kiện hợp lệ dùng chung cho MỌI cặp occupation cùng Section

```text
SectionReleaseHeadwayMinutes = 3     // HeadwayRules.SectionReleaseHeadwayMinutes, mục 5.6 — ÁP DỤNG
                                      // GIỐNG HỆT cho cả cặp cùng chiều lẫn ngược chiều trên cùng Section
```

Cho hai occupation `A`, `B` bất kỳ trên cùng `Section` (không phân biệt chiều), gọi `Earlier` = occupation
có `EntryTime` nhỏ hơn, `Later` = occupation còn lại:

```text
ActualGapMinutes = Later.EntryTime − Earlier.ExitTime

ActualGapMinutes >= SectionReleaseHeadwayMinutes   → HỢP LỆ, không phải Conflict
ActualGapMinutes <  SectionReleaseHeadwayMinutes   → Conflict (loại cụ thể xem mục 5.3/5.4)
```

`ActualGapMinutes` **có thể âm** — nghĩa là hai occupation thực sự chồng lấp thời gian trên cùng Section
(`Later.EntryTime < Earlier.ExitTime`), không chỉ "chưa đủ giãn cách". Cả hai trường hợp (gap dương nhưng
thiếu, và gap âm/overlap thật) đều đi qua **cùng một phép so sánh** — không có nhánh riêng cho
"entry-entry" hay "exit-exit" như bản nháp trước.

**Biên quan trọng — `ActualGapMinutes == 0` VẪN LÀ conflict:** `A.ExitTime == B.EntryTime` không phải
overlap khoảng thời gian theo nghĩa toán học (`[A.Entry,A.Exit)` và `[B.Entry,B.Exit)` không giao nhau),
nhưng vẫn thiếu đủ 3 phút release headway → vẫn phải là `Conflict`. Đây là lý do quy tắc mục 5 **không
được** viết lại thành `overlap(A,B) := max(...) < min(...)` như bản nháp trước — điều kiện đó bỏ sót đúng
case biên này.

`ConstraintKind` (mục 1.7) suy ra trực tiếp từ dấu của `ActualGapMinutes`:

```text
ActualGapMinutes < 0   → ConstraintKind = SectionOverlap        // hai occupation THỰC SỰ chồng lấp
0 <= ActualGapMinutes < SectionReleaseHeadwayMinutes
                        → ConstraintKind = SectionReleaseHeadway // không chồng lấp, chỉ thiếu headway
ActualGapMinutes >= SectionReleaseHeadwayMinutes
                        → không tạo Conflict
```

```text
HeadwayDeficitMinutes = max(0, SectionReleaseHeadwayMinutes − ActualGapMinutes)
```

Với `ActualGapMinutes` âm (overlap thật), `HeadwayDeficitMinutes` tự động > `SectionReleaseHeadwayMinutes`
(vd. overlap sâu 2 phút → deficit = 3 − (−2) = 5) — không cần công thức riêng cho case overlap.

### 5.3 Phân loại theo chiều — MEET (ngược chiều) vs HEADWAY (cùng chiều)

`ConstraintKind` và `Type` được suy **độc lập với nhau** từ hai thứ khác nhau — `ConstraintKind` chỉ phụ
thuộc dấu của `ActualGapMinutes` (mục 5.2), `Type` chỉ phụ thuộc chiều của `A`/`B`. Không có quy tắc nào
gán cứng "`SectionOverlap` chỉ xảy ra với `MEET`" — cùng chiều **vẫn có thể** `ActualGapMinutes < 0` (tàu
sau vào Section trước khi tàu trước ra hẳn, chồng lấp vật lý thật trên cùng khu gian dù cùng chiều), và
khi đó vẫn là `Type=HEADWAY` (vì cùng chiều), chỉ có `ConstraintKind=SectionOverlap` (vì gap âm):

```text
if ActualGapMinutes >= SectionReleaseHeadwayMinutes:
    không tạo Conflict
else:
    ConstraintKind = (ActualGapMinutes < 0) ? SectionOverlap : SectionReleaseHeadway
    Type           = (A.Direction != B.Direction) ? MEET : HEADWAY
```

Ví dụ (đã sửa sau review, khác bản nháp trước — bản trước lầm tưởng "HEADWAY cùng chiều không có khả
năng overlap kiểu MEET", **sai**):

```text
Cùng chiều, ActualGap = -2   → Type=HEADWAY, ConstraintKind=SectionOverlap, HeadwayDeficit = 3-(-2) = 5
Cùng chiều, ActualGap =  0   → Type=HEADWAY, ConstraintKind=SectionReleaseHeadway
Cùng chiều, ActualGap =  2   → Type=HEADWAY, ConstraintKind=SectionReleaseHeadway
```

**Ngoại lệ double-track (chưa có track assignment ở Phase 3):** rule ở trên chỉ áp dụng đầy đủ khi
`Section.NumberOfTracks == 1`. Khi `Section.NumberOfTracks >= 2`, **không** báo `MEET` chỉ vì hai
occupation ngược chiều trùng/gần thời gian — vì rất có thể chúng dùng hai track vật lý độc lập, và
Phase 3 hiện tại **chưa có track assignment** để biết chắc. HEADWAY (cùng chiều) **vẫn áp dụng như
thường** kể cả khi `NumberOfTracks >= 2` (không có ngoại lệ ở đây, vì mặc định thận trọng: vẫn có khả
năng hai tàu cùng chiều dùng chung track). Kiến trúc để ngỏ: khi có track assignment thật, rule sẽ áp
theo đúng track/resource mà occupation sử dụng thay vì mù theo toàn `Section` — không đổi công thức gap
ở mục 5.2, chỉ đổi phạm vi "occupation nào được so với occupation nào" (theo track thay vì theo Section).

### 5.4 OVERTAKE (cùng chiều, thứ tự bị đảo qua nhiều ga/khu gian)

`HEADWAY` (mục 5.3) đã bắt được mọi vi phạm gap tại **một** Section — kể cả trường hợp gap âm sâu (tàu
sau vào Section trước khi tàu trước ra hẳn). Điều `HEADWAY` chưa nói được: liệu chỉ cần tàu sau **chờ
thêm** tại ga trước Section này là đủ giải quyết (đây vẫn là `HEADWAY` thuần), hay tốc độ nội tại của hai
tàu khiến vi phạm **lặp lại/nặng thêm ở các Section kế tiếp** dù có chờ thêm bao nhiêu — trường hợp sau
chỉ giải được bằng cách cho tàu nhanh **vượt hẳn** tại một ga có năng lực vượt, không phải chờ đơn thuần.

Thuật toán (không đổi so với bản nháp trước, vẫn đúng logic): đi dọc các ga chung của A, B theo chiều di
chuyển; theo dõi "ai đang ở phía trước theo thời gian" tại mỗi ga; nếu tại ga `p` train A đang trước
(`Arrival(A,p) < Arrival(B,p)`) nhưng tại ga `q > p` thứ tự đảo ngược liên tục qua nhiều Section kế tiếp
(không chỉ 1 Section như `HEADWAY` đơn lẻ) → đây là vùng cần `OVERTAKE`, đánh dấu dải Section `[p, q]` là
vùng xung đột loại này, với `ConstraintKind = OrderReversal`.

`ConflictAnalyzer` (Phase 6) **luôn tạo `HEADWAY` trước** (mục 5.3, tại đúng Section phát hiện gap thiếu),
rồi mới xét tiếp các Section kế cận cùng cặp A/B để xác nhận có phải `OrderReversal` kéo dài không; nếu
có, phân loại lại `Type = OVERTAKE` (thay cho chuỗi `HEADWAY` liên tiếp) trước khi đưa vào
`CandidateGenerator` — tránh sinh hàng loạt candidate "chờ tại ga" vô nghĩa cho một vấn đề chỉ giải được
bằng vượt.

Cả `MEET`, `HEADWAY`, `OVERTAKE` dùng chung interface `IConflictRule.Detect(occupations) -> List<Conflict>`
để `ConflictDetector` chạy nhiều rule song song trên cùng dữ liệu occupation, đúng yêu cầu kiến trúc mở ở
mục 14. Độ phức tạp: O(n log n) mỗi section (n = số occupation, sort theo `EntryTime` rồi sweep các cặp kề
nhau — không cần so mọi cặp `O(n²)`, vì occupation không "chèn" giữa hai occupation liền kề mà không vi
phạm gap với ít nhất một trong hai), tổng O(N log N) toàn tuyến.

### 5.5 Thuật toán tổng hợp (`ConflictDetector`, mỗi `Section` độc lập)

```text
foreach Section S:
    occupations := tất cả SectionOccupation trên S (mọi service × mọi CycleIndex trong cửa sổ, mục 5.1)
    sort occupations theo EntryTime
    foreach cặp (Earlier, Later) KỀ NHAU theo EntryTime (và các cặp gần kề khác có thể còn vi phạm — xem
                                                          ghi chú độ phức tạp mục 5.4):
        if S.NumberOfTracks >= 2 and Earlier.Direction != Later.Direction:
            continue                                    // ngoại lệ double-track, mục 5.3

        ActualGapMinutes := Later.EntryTime - Earlier.ExitTime
        if ActualGapMinutes >= SectionReleaseHeadwayMinutes:
            continue                                    // hợp lệ

        ConstraintKind := ActualGapMinutes < 0 ? SectionOverlap : SectionReleaseHeadway
        HeadwayDeficitMinutes := SectionReleaseHeadwayMinutes - ActualGapMinutes
        Type := (Earlier.Direction != Later.Direction) ? MEET : HEADWAY
        emit Conflict { Type, ConstraintKind, SectionId=S, ActualGapMinutes, HeadwayDeficitMinutes,
                         RequiredHeadwayMinutes=SectionReleaseHeadwayMinutes, ... (mục 1.7) }

// Bước riêng, SAU khi đã có toàn bộ Conflict HEADWAY (mục 5.4):
foreach cặp (A, B) cùng chiều có ít nhất 1 Conflict HEADWAY chung:
    kiểm tra OrderReversal qua các Section kế tiếp — nếu xác nhận, gộp/phân loại lại thành OVERTAKE
```

### 5.6 `HeadwayRules` (Configuration) — hợp nhất theo quy tắc Section Release Headway

```text
IHeadwayRules
  SectionReleaseHeadwayMinutes: int = 3   // DÙNG CHUNG cho MEET (ngược chiều) và HEADWAY (cùng chiều),
                                           // đúng nghiệp vụ mục 5.2 — KHÔNG tách MeetHeadway/
                                           // SameDirectionHeadway thành 2 số khác nhau nữa.
  OvertakeHeadway: int                    // GIỮ RIÊNG — dùng ở mục 7.2 (RequiredShiftCalculator,
                                           // khoảng cách departure sau khi vượt tại ga), khác ngữ cảnh
                                           // với Section Release Headway (đang xét khu gian, không phải
                                           // thời điểm khởi hành sau khi overtake). CHƯA rà soát lại
                                           // công thức mục 7.2 trong lượt sửa này — để dành khi code
                                           // RequiredShiftCalculator thật (Phase 3 resolver), tránh lấn
                                           // phạm vi ngoài yêu cầu hiện tại (chỉ ConflictDetector).
```

Đã giải quyết câu hỏi mở #3 ở mục 14.2 (*"`SameDirectionHeadway` mặc định — có thể tạm dùng chung giá trị
với `MeetHeadway`, nhưng nên xác nhận"*): xác nhận xong — không phải "tạm dùng chung", mà **là cùng một
khái niệm** (`SectionReleaseHeadwayMinutes`) theo đúng nghiệp vụ, không có 2 hằng số riêng để có thể lệch
nhau trong tương lai.

### 5.7 Bộ test bắt buộc cho `ConflictDetector` (spec — chưa có code, dùng khi viết `ConflictDetectorTests` ở Phase 3)

Toàn bộ scenario dưới đây dùng `SectionReleaseHeadwayMinutes = 3` (mục 5.6), 1 `Section` duy nhất trừ khi
ghi chú khác, đơn vị phút tuyệt đối (mục 2).

**Ngược chiều (MEET):**

| # | ExitA | EntryB | ActualGap | Kết quả |
|---|-------|--------|-----------|---------|
| 1 | 100 | 103 | 3 | Không tạo `Conflict` (hợp lệ, đúng ngưỡng) |
| 2 | 100 | 102 | 2 | `MEET` / `ConstraintKind=SectionReleaseHeadway`, `HeadwayDeficitMinutes=1` |
| 3 | 100 | 100 | 0 | `MEET` / `ConstraintKind=SectionReleaseHeadway`, `HeadwayDeficitMinutes=3` (biên — KHÔNG phải overlap toán học, vẫn là Conflict, xem mục 5.2) |
| 4 | occupation A/B chồng lấp khoảng thời gian thật (`ActualGap<0`, vd `EntryB=98 < ExitA=100`) | — | `<0` | `MEET` / `ConstraintKind=SectionOverlap`, `HeadwayDeficitMinutes = 3 + |ActualGap|` |

**Cùng chiều (HEADWAY):**

| # | ExitA | EntryB | ActualGap | Kết quả |
|---|-------|--------|-----------|---------|
| 5 | 100 | 103 | 3 | Không tạo `Conflict` |
| 6 | 100 | 102 | 2 | `HEADWAY` / `ConstraintKind=SectionReleaseHeadway` |
| 7 | 100 | 100 | 0 | `HEADWAY` / `ConstraintKind=SectionReleaseHeadway` (biên, giống case 3 nhưng `Type` khác vì cùng chiều) |
| 7b | occupation A/B **cùng chiều** chồng lấp khoảng thời gian thật (`ActualGap<0`, vd `EntryB=98 < ExitA=100`, `ActualGap=-2`) | — | `<0` | `HEADWAY` / `ConstraintKind=SectionOverlap`, `HeadwayDeficitMinutes=5` — **quan trọng**: `SectionOverlap` KHÔNG chỉ xảy ra với `MEET` (đã sửa sau review, xem mục 5.3) |

**Cyclic (xuyên biên chu kỳ, mục 11):**

| # | Kịch bản | Kết quả |
|---|----------|---------|
| 8 | `A` (CycleIndex=0) có `ExitTime=1439`; `B` (CycleIndex=+1) có `EntryTime` tại chu kỳ 0 là `1` → tuyệt đối `= 1 + 1×1440 = 1441` → `ActualGap = 1441-1439 = 2` | Phải detect `Conflict` (MEET hoặc HEADWAY tuỳ chiều) — đúng thuật toán mục 11.3 (occupations sinh cho mọi `CycleIndex ∈ [-K,K]` TRƯỚC khi sweep, không xử lý biên chu kỳ như một case riêng) |

**Double-track (mục 5.3 ngoại lệ):**

| # | Kịch bản | Kết quả |
|---|----------|---------|
| 9 | `Section.NumberOfTracks = 2`, hai occupation ngược chiều có `ActualGap < 0` (overlap nếu coi là single-track) | KHÔNG tạo `MEET` (bỏ qua cặp ngược chiều khi `NumberOfTracks >= 2`, mục 5.3) |
| 10 | `Section.NumberOfTracks = 2`, hai occupation **cùng chiều** có `ActualGap < 3` | VẪN tạo `HEADWAY` như bình thường (không có ngoại lệ double-track cho cùng chiều, mục 5.3) |

Case 1–7 ánh xạ trực tiếp từ ví dụ bạn đưa khi yêu cầu sửa rule này; case 7b bổ sung sau review thứ 2
(cùng chiều vẫn có thể `SectionOverlap`, mục 5.3); case 8–10 bổ sung để khép kín với mục 11 (cyclic) và
ngoại lệ double-track (mục 5.3) đã có sẵn trong thiết kế nhưng chưa từng có test đi kèm.

---

## 6. Candidate Generation (mục 11, 14 — câu hỏi 8)

Với một `Conflict` tại Section `(i, i+1)`:

```text
CandidateWindow (config, ví dụ 3) →
  candidate stations = { i-window .. i+1+window } ∩ { ga có CanMeet (nếu Type=MEET)
                                                        hoặc CanOvertake (nếu Type=OVERTAKE/HEADWAY) }
                        ∩ { ga nằm trong route còn lại của CẢ HAI tàu, chưa đi qua }
```

Với mỗi candidate station `S`, sinh **2 CandidateSolution** (train nào chờ):
`{Conflict, CandidateStation=S, TrainToWait=A}` và `{..., TrainToWait=B}`.

Prefilter rẻ trước khi tính RequiredShift đầy đủ: loại ngay candidate nếu
`ForwardSlack(TrainToWait, S) < 0` (mục 4.1) hoặc `S` không nằm giữa vị trí hiện tại và đích của tàu đó —
tránh tính toán thừa (liên quan tới yêu cầu hiệu năng mục 29).

Lưu ý: `Conflict` tham chiếu tới một cặp **instance** cụ thể (`ServiceA/CycleIndexA`,
`ServiceB/CycleIndexB` — mục 1.7), nhưng `TrainToWait` trong `CandidateSolution` luôn được hiểu là
"`TrainService` tương ứng của instance đó" — vì quyết định ghi luôn vào `TrainService` (mục 0.1). Thời
điểm `Arrival/Departure` dùng trong các công thức mục 7 phải lấy từ instance (đã cộng `CycleIndex×1440`),
rồi khi tính `RequiredShift` xong mới quy đổi ngược lại thành delta áp vào `TrainService` (delta là bất
biến qua phép dịch nên không cần trừ lại `CycleIndex×1440` — chỉ *đọc* qua instance, *ghi* delta thuần tuý
vào service).

---

## 7. RequiredShiftCalculator (mục 12 — câu hỏi 9)

Đây là hàm nghiệp vụ trung tâm, tách bạch theo `Conflict.Type`.

### 7.0 Quyết định kiến trúc (sau review Phase 2): KHÔNG gộp Accel/Decel/Waiting thành một scalar

`ForcedStop` sinh ra **hai loại thay đổi khác nhau về bản chất vật lý** trên trajectory, xảy ra ở
**hai vị trí khác nhau**, và **không được cộng gộp thành một con số `delayMinutes` duy nhất rồi chèn
tại một điểm** (rủi ro cụ thể: Arrival(S) bị cộng nhầm cả AccelerationPenalty dù gia tốc chỉ xảy ra ở
khu gian S→S+1 *sau* ga S; RunningTimeFromPrev(S) và RunningTimeFromPrev(S+1) không phản ánh đúng vị
trí penalty; section occupation dùng cho `ConflictDetector` (mục 1.6) vì vậy sai theo):

```text
Section S-1 → S      Ga S            Section S → S+1
      +1'            (dừng)               +2'
  DecelerationPenalty              AccelerationPenalty
```

1. **Structural stop mutation** — hệ quả *vật lý* của việc chuyển S từ Through sang có dừng, không phụ
   thuộc headway với tàu kia, cố định vị trí trên trajectory:

   ```text
   ApplyForcedStop(T, S):
       StopType(S)                := ForcedMeet | ForcedOvertake
       RunningTimeFromPrev(S)     += DecelerationPenalty     // khu gian (S-1 → S)
       RunningTimeFromPrev(S+1)   += AccelerationPenalty     // khu gian (S → S+1)
       // Arrival(S) tăng đúng DecelerationPenalty; từ S+1 trở đi, phần dôi ra
       // (DecelerationPenalty + AccelerationPenalty) lan truyền xuôi dòng bằng
       // ĐÚNG cơ chế propagation ở mục 8 (hấp thụ dần vào RecoveryTimeFromPrev phía sau),
       // neo carry bắt đầu từ S+1 — KHÔNG neo tại S, vì DecelerationPenalty đã "tiêu" cục bộ
       // ngay trong RunningTimeFromPrev(S) chứ chưa cần propagate.
   ```

2. **Operational waiting mutation** — hệ quả từ ràng buộc headway với tàu kia, chèn đúng tại ga S,
   dùng **nguyên trạng** `TrajectoryPropagator.InsertDelay` (mục 8) trên trajectory ĐÃ được
   `ApplyForcedStop` cập nhật (vì `NaturalDeparture(W,S)` ở dưới phải đọc `Arrival(S)` sau khi đã cộng
   `DecelerationPenalty`, nếu không `ExtraWait` sẽ bị tính thiếu):

   ```text
   RequiredWaitingMinutes := ExtraWait(W, S)      // công thức 7.1/7.2 bên dưới
   InsertDelayAtStation(T, S, RequiredWaitingMinutes)
   ```

`TrajectoryPropagator` (Phase 2, đã chốt) giữ nguyên: nó **không** tự suy luận ForcedStop/MEET/OVERTAKE,
chỉ nhận một con số và một điểm neo rồi lan truyền thuần tuý. Cả hai bước trên đều là **caller** của
cùng một primitive propagation, khác nhau ở: (a) trajectory đầu vào (trước/sau `ApplyForcedStop`), (b)
điểm neo carry (S+1 cho structural, S cho operational).

**Thứ tự bắt buộc**: `ApplyForcedStop` phải chạy **trước** khi tính `NaturalDeparture`/`ExtraWait`, và cả
hai mutation phải được coi là **atomic đối với một candidate** — `RequiredShiftCalculator` chỉ *tính*
(không mutate trajectory thật), trả về đủ dữ liệu để một `CandidateApplicator`/`TrajectoryMutator` ở tầng
gọi (Phase 6/7) áp dụng cả hai theo đúng thứ tự MỘT LẦN khi candidate được chọn — tránh double-apply nếu
candidate bị đánh giá nhiều lần trong beam search mà không được chọn.

```text
RequiredShiftResult
{
    IsFeasible: bool
    IsForcedStop: bool                 // = ForcedStop(W,S)
    DecelerationPenaltyMinutes: int    // 0 nếu !IsForcedStop
    AccelerationPenaltyMinutes: int    // 0 nếu !IsForcedStop
    RequiredWaitingMinutes: int        // = ExtraWait(W,S), tính SAU khi áp structural (nếu có)
    TotalAdditionalTimeMinutes: int    // = Decel + Accel + RequiredWaiting — dùng cho điều kiện 6, mục 7.3
    ViolatedConstraint: string?
}
```

(Thay cho `ShiftResult { IsFeasible, ShiftMinutes, ForcedStop, ViolatedConstraint? }` ở bản nháp trước —
`ShiftMinutes` đơn lẻ không đủ để `CandidateApplicator` biết áp penalty ở đâu.)

### 7.1 MEET tại ga S, tàu chờ = W, tàu đi qua = P

```text
ForcedStop(W, S) = (S không thuộc StopRequirements của W)   // true nếu W vốn chạy thông qua S

// Bước 1 (nếu ForcedStop): ApplyForcedStop(T, S) — xem mục 7.0 — rồi mới đọc Arrival(W,S) bên dưới.

EarliestSafeDeparture(W, S) = Arrival(P, S) + SectionReleaseHeadwayMinutes
                             // = ĐÚNG rule Section Release Headway (mục 5.2/5.6): Arrival(P,S) chính là
                             // ExitTime của P khỏi khu gian dẫn vào S; W muốn đi khu gian đó theo chiều
                             // ngược lại (departure từ S) phải cách đủ SectionReleaseHeadwayMinutes.
                             // Đổi tên từ "MeetHeadway" (bản nháp trước) — cùng một hằng số, chỉ đổi tên
                             // cho khớp mục 5.6 sau khi xác nhận đây là Section Release Headway dùng
                             // chung, không phải một loại headway riêng cho MEET.
NaturalDeparture(W, S)      = Arrival(W,S, SAU structural nếu có) + StopTime(W,S,tự nhiên hoặc 0 nếu qua thông)

ExtraWait(W, S) = MAX(0, EarliestSafeDeparture(W,S) - NaturalDeparture(W,S))

RequiredWaitingMinutes(W) = ExtraWait(W, S)   // bước 2 (operational) — xem mục 7.0

TotalAdditionalTimeMinutes(W) =
    RequiredWaitingMinutes(W)
  + [ ForcedStop(W,S) ?  AccelerationPenalty + DecelerationPenalty  : 0 ]
```

Lưu ý chiều: nếu `P` đến trước và `W` vốn phải chờ P thì trên — đây là case điển hình khi W đang đỗ sẵn
tại S chờ đi tiếp. Nếu ngược lại (W đã ở trong section muốn tới S nhưng P đến S trước và cần W dừng hẳn
lại thay vì chạy tiếp), công thức đối xứng: ràng buộc trở thành trên cạnh vào S của W — cùng một pattern,
chỉ đổi biến nào là "đến trước/đi sau". `RequiredShiftCalculator` implement như 1 phép so khớp cặp
(Arrival/Departure) theo đúng quan hệ trong mục 6, tổng quát hoá cho cả 2 hướng thứ tự trước–sau.

### 7.2 OVERTAKE tại ga S, tàu chậm = Slow (chờ), tàu nhanh = Fast (vượt)

```text
NaturalDeparture(Fast, S) = ... (không đổi, Fast không bị ảnh hưởng nếu S có đủ track vượt)
// Bước 1 (nếu ForcedStop(Slow,S)): ApplyForcedStop(T, S) trước, rồi mới đọc NaturalDeparture(Slow,S).
EarliestSafeDeparture(Slow, S) = Departure(Fast, S) + OvertakeHeadway
RequiredWaitingMinutes(Slow) = MAX(0, EarliestSafeDeparture(Slow,S) - NaturalDeparture(Slow,S))
TotalAdditionalTimeMinutes(Slow) = RequiredWaitingMinutes(Slow) + [ForcedStop(Slow,S) ? Accel+Decel : 0]
```

Điều kiện tiên quyết: `Arrival(Fast, S) < Departure(Slow, S) tự nhiên` (Fast phải đến kịp trước khi Slow
định rời ga) — nếu không, ga này không giải quyết được overtake, loại khỏi candidate list.

### 7.3 Kiểm tra tính hợp lệ sau khi có RequiredShift (bắt buộc, 7 điều kiện mục 12)

```text
1–2. Không còn overlap MEET / vi phạm headway tại chính section đang xét    → đảm bảo bởi công thức trên.
3.   StopTime >= MandatoryStopTime nếu ga đó vốn đã bắt buộc dừng           → giữ nguyên min, chỉ kéo dài.
4.   Accel/Decel cộng đúng nếu ForcedStop=true                              → đã tính ở trên.
5.   FixedDepartureTime tại ga xuất phát KHÔNG đổi                          → shift chỉ áp dụng từ ga hiện
                                                                                tại trở đi, không lùi về gốc.
6.   TotalAdditionalTimeMinutes(W) <= ForwardSlack(W, S)  (mục 4.1)          → nếu vượt, candidate
                                                                                infeasible theo mặc định
                                                                                (Phase 5-7); chỉ Phase 8
                                                                                mới thử mượn thêm
                                                                                RedistributableSlack (mục
                                                                                4.2). Lưu ý: so sánh dùng
                                                                                TỔNG (Decel+Accel+Waiting,
                                                                                mục 7.0), không chỉ phần
                                                                                waiting — nếu chỉ so
                                                                                waiting thì structural
                                                                                mutation có thể tự nó đã
                                                                                đẩy tàu trễ FixedArrivalTime
                                                                                mà không bị chặn.
7.   Không tạo xung đột "không thể giải" phía sau                          → KHÔNG do hàm này tự đảm bảo,
                                                                                mà do bước Rolling-Horizon
                                                                                re-check (mục 9, 14) làm sau
                                                                                khi propagate — tách trách
                                                                                nhiệm rõ ràng. Vì decision
                                                                                ghi vào TrainService (mục
                                                                                0.1), re-check này phải quét
                                                                                LẠI TOÀN BỘ instance của W
                                                                                trong cửa sổ chu kỳ, không
                                                                                chỉ đúng instance đã xung đột
                                                                                (xem mục 9.2).
```

`RequiredShiftCalculator` trả về `RequiredShiftResult` (mục 7.0) — không tự quyết định chọn, cũng
**không tự mutate trajectory**, chỉ tính toán chính xác cho `CandidateEvaluator` dùng; việc mutate là
trách nhiệm của `CandidateApplicator`/`TrajectoryMutator` (mục 7.0) khi candidate được chọn.

**Vì sao điều kiện 6 (`TotalAdditionalTimeMinutes <= ForwardSlack`) đủ để đảm bảo điều kiện 5 và tính hấp
thụ được ở mục 8:** theo định nghĩa `ForwardSlack(T,k) = FixedArrivalTime(T) - CurrentDeparture(T,k) -
MinimumRemainingJourneyTime(T,k→dest)`, nếu `delta <= ForwardSlack(T,k)` thì kể cả trong tình huống xấu
nhất — bỏ hết mọi recovery-time đã hoạch định ở các ga sau `k` và chạy đúng bằng tối thiểu suốt phần còn
lại — tàu vẫn đến đích không muộn hơn `FixedArrivalTime(T)`. Điều này đúng bất kể `delta` đó là một
`RequiredWaitingMinutes` chèn tại `S` hay là `DecelerationPenalty+AccelerationPenalty` chèn (thực chất)
từ `S+1` — vì `ForwardSlack` không quan tâm delta "được neo ở đâu trong khoảng [k, S]", chỉ quan tâm
tổng cộng dồn từ `k` trở đi có vượt quá phần đệm còn lại hay không. Đây chính là lý do thuật toán
propagation ở mục 8 luôn tìm được cách hấp thụ hết `delta` mà **không cần biết trước** buffer downstream
được phân bố cụ thể ra sao — nó chỉ cần tồn tại (dưới dạng recovery đã cấy, hoặc dưới dạng "chưa cấy
nhưng vẫn nằm trong biên `MinimumJourneyTime`") đủ nhiều theo đúng số học ở trên.

---

## 8. Propagation dọc trajectory (mục 19 — câu hỏi 12)

`TrajectoryPropagator.InsertDelay(T, k, delta)` (Phase 2, đã implement — xem
`src/TrainTimetable.Engine/TrajectoryPropagator.cs`) là **primitive duy nhất** thực hiện lan truyền dưới
đây; nó nhận một điểm neo `k` và một `delta` thuần túy, không biết `delta` đến từ đâu:

```text
delta := input
for station m from k to Destination(T):
    Departure(m) += delta          // (nếu m == k: set theo giá trị mới; nếu m > k: cộng dồn)
    plannedRecovery = RecoveryTimeFromPrev(m+1)
    if plannedRecovery >= delta:
        RecoveryTimeFromPrev(m+1) -= delta
        delta := 0
        break                       // hấp thụ hết, dừng lan truyền sớm — quan trọng cho hiệu năng
    else:
        delta -= plannedRecovery
        RecoveryTimeFromPrev(m+1) := 0
        Arrival(m+1) tăng theo phần delta còn lại
continue cho tới khi delta == 0 hoặc chạm Destination
```

Theo kiến trúc mục 7.0, một `ForcedStop` tại ga `S` gọi primitive này **hai lần**, với hai điểm neo khác
nhau, theo đúng thứ tự:

```text
1. ApplyForcedStop(T, S):
     RunningTimeFromPrev(S)   += DecelerationPenalty
     RunningTimeFromPrev(S+1) += AccelerationPenalty
     InsertDelay(T, k=S+1, delta=DecelerationPenalty+AccelerationPenalty)   // neo tại S+1, xem mục 7.0
2. InsertDelay(T, k=S, delta=RequiredWaitingMinutes)                        // neo tại S
```

(Nếu conflict không phải MEET/OVERTAKE mà chỉ là delay vận hành thuần túy — vd tàu trễ do sự cố — thì
chỉ có bước 2 chạy, với `S` = ga phát sinh delay; đây chính là use case gốc mà `TrajectoryPropagatorTests`
đang kiểm chứng ở Phase 2.)

Vì `TotalAdditionalTimeMinutes` đã được kiểm tra `<= ForwardSlack` (mục 7.3, điều kiện 6) trước khi commit
— tính trên **tổng** cả hai lần gọi — về mặt lý thuyết `delta` ở cả hai bước cộng lại sẽ luôn được hấp thụ
hết trước khi chạm `FixedArrivalTime`, cho dù bước 1 (neo tại S+1) tiêu một phần recovery trước khi bước 2
(neo tại S) chạy tới. Invariant này chính là điều `RequiredShiftCalculator` phải đảm bảo (mục 7.3, điều
kiện 6), và Phase 2 `TrajectoryPropagator` tự nó **không cần biết** có đang xử lý ForcedStop hay không —
đúng như đã chốt trong review Phase 2 (`TrajectoryPropagator` giữ đơn giản, không tự suy luận nghiệp vụ
tránh/vượt).

**Early-exit** khi `delta == 0` là mấu chốt cho yêu cầu hiệu năng ở mục 29: trong đa số trường hợp buffer
được rải hợp lý, propagation chỉ chạm vài ga chứ không phải toàn bộ phần còn lại của route.

Sau propagation, chỉ các Section có occupation thay đổi (của tàu `W`) mới cần đưa vào lại
`ConflictDetector` — đây là cơ sở cho **conflict index theo (SectionId, đoạn thời gian)** để invalidate
có chọn lọc (mục 29), thay vì quét lại toàn tuyến.

**Điểm quan trọng vì decision variable thuộc `TrainService` (mục 0.1):** phép propagation trên áp dụng
lên `TrainServiceTrajectory` (chu kỳ 0), tức là áp dụng **đồng thời và giống hệt** lên mọi
`TrainInstance(ServiceId=W, CycleIndex=n)` với mọi `n` (vì instance chỉ là service dịch `+n×1440`, không
lưu trạng thái riêng — mục 1.4). Do đó, sau propagation, `RecalculateAffected` **không chỉ re-check các
Section bị đổi của instance đã gây ra conflict ban đầu**, mà phải re-check các Section đó cho **toàn bộ
`CycleIndex` trong cửa sổ `[-K, K]`** của service `W` — vì rất có thể instance `W/(d+1)` hoặc `W/(d-1)`
(trước đây không xung đột với ai) nay lại chồng lấp với một service khác do cùng bị dịch. Đây chính là hệ
quả 3 đã nêu ở mục 0.1, và là lý do `ConflictDetector.DetectIncremental` ở mục 9.2 nhận `scope` là
**(ServiceId, mọi CycleIndex trong K)**, không phải một cặp instance đơn lẻ.

---

## 9. SearchState & Beam Search (mục 17–18 — câu hỏi 11, 13)

### 9.1 Cấu trúc State — persistent / delta-based, không deep-clone

```text
SearchState
  ParentState: SearchState?              // null nếu là root (minimum timetable ban đầu)
  Decision: CandidateSolution?            // quyết định dẫn từ Parent → State này (null ở root)
  LocalDelta: Map<ServiceId, List<TimetableEntryOverride>>  // CHỈ TrainService bị đổi — KHÔNG BAO GIỜ
                                                             // keyed theo (ServiceId, CycleIndex): decision
                                                             // variable chỉ thuộc TrainService (mục 0.1).
  ResolvedConflictIds: Set<ConflictId>    // ConflictId tham chiếu cặp instance cụ thể đã resolve
  NewlyIntroducedConflicts: List<Conflict> // do FutureConflictImpact check sinh ra ở tầng này
  BufferUsageDelta: Map<ServiceId, int>    // chỉ service bị đổi ở tầng này
  ForcedStopsAdded: List<(ServiceId, StationSeq)>
  Cost: float                              // cộng dồn từ Parent.Cost + IncrementalCost(Decision)
  Depth: int
```

Đây là mô hình **cây version / zipper**: mỗi node chỉ giữ *diff* so với cha, không copy toàn bộ 178 ga ×
N tàu. Để lấy trajectory đầy đủ của 1 tàu tại 1 state: đi ngược `ParentState` chain, áp `LocalDelta` theo
thứ tự từ root xuống (hoặc cache "materialized view" theo kiểu memoization — chỉ materialize khi
`CandidateEvaluator` thực sự cần đọc, và chỉ cho (các) tàu liên quan, không phải toàn bộ tàu trong hệ
thống).

Clone = tạo 1 `SearchState` mới trỏ `ParentState` = state hiện tại + `LocalDelta` mới → **O(1) theo số
tàu bị ảnh hưởng**, không phụ thuộc 178 ga. Rollback = đơn giản không đi theo nhánh đó nữa (không cần thao
tác undo vì không gì bị mutate in-place).

Để tránh chuỗi `ParentState` dài vô hạn làm chậm việc materialize (sau nhiều tầng beam search), có thể định
kỳ (mỗi K tầng) "nén" (flatten) một nhánh sống sót thành baseline mới — nhưng đây là tối ưu hoá kỹ thuật,
để dành tới Phase 7 khi có số liệu profiling thật.

### 9.2 Beam Search loop (Phase 7)

```text
frontier := { RootState (= minimum timetable) }
while còn unresolved conflict trong bất kỳ state nào của frontier, và chưa vượt LookAheadConflicts theo Depth:
    nextFrontier := []
    for state in frontier:
        conflict := state.NextUnresolvedConflict()   // conflict "gần nhất" theo thời gian/vị trí
        candidates := CandidateGenerator(conflict, state)
        for candidate in candidates:
            shift := RequiredShiftCalculator(candidate, state)     // đọc qua instance, tính theo mục 7
            if not shift.IsFeasible: continue
            newState := ApplyCandidate(state, candidate, shift)      // ghi delta vào TrainService, O(1)-ish
            RecalculateAffected(newState)                             // propagate (mục 8) trên
                                                                       // TrainServiceTrajectory của
                                                                       // service vừa đổi
            // scope KHÔNG phải 1 cặp instance — phải là (ServiceId vừa đổi, mọi CycleIndex ∈ [-K,K])
            // so với MỌI service khác, đúng hệ quả 3 mục 0.1 / lưu ý cuối mục 8:
            newConflicts := ConflictDetector.DetectIncremental(newState, scope=(ServiceId, AllCycles))
            newState.Cost := state.Cost + CostFunction(candidate, shift, newConflicts)  // mục 10
            nextFrontier.add(newState)
    frontier := TopN(nextFrontier, BeamWidth, keyBy=Cost)   // giữ N state tốt nhất
    if frontier rỗng: → BÁO INFEASIBLE với trace các nhánh bị cắt (để debug, mục 25)
return BestState(frontier) sau khi hết unresolved conflict (hoặc sau khi optimize toàn cục ở Phase 8)
```

`BeamWidth`, `LookAheadConflicts`, `CandidateWindow` đều đọc từ `SolverParameters`, không hard-code.

---

## 10. Difficulty & Cost (mục 15–16 — câu hỏi 9,14 liên quan)

```text
Difficulty(conflict, candidate) =
      w1 × ( RequiredShift / max(AvailableUsableSlack, ε) )
    + w2 × NetworkImpact(candidate)     // vd: số tàu khác có occupation gần candidate station/time window

Cost(candidate) =
      α × WaitingTime
    + β × ( RequiredShift / max(ForwardSlack(TrainToWait, S), ε) )   // BufferConsumptionRatio — dùng
                                                                       // ForwardSlack cục bộ (mục 4.1),
                                                                       // KHÔNG dùng RemainingBuffer toàn
                                                                       // cục — đúng nguyên tắc mục 4.3.
    + γ × ForcedStopPenalty            // hằng số phạt nếu ForcedStop=true
    + δ × PriorityPenalty(TrainToWait) // vd tàu ưu tiên cao bị bắt chờ → phạt nặng hơn
    + ε × FutureConflictImpact
```

### FutureConflictImpact (câu hỏi 14)

Sau khi áp `candidate` (propagation xong), chạy `ConflictDetector` **chỉ trong rolling horizon**: giới hạn
theo số ga phía trước (`LookAheadStations`, config) hoặc số conflict kế tiếp (`LookAheadConflicts`) tính
từ (các) tàu vừa bị dịch chuyển. Với mỗi conflict mới phát hiện trong horizon này, ước lượng nhanh (KHÔNG
đệ quy giải nó) một `Difficulty` xấp xỉ (dùng slack hiện có mà chưa cần tìm candidate cụ thể) rồi cộng dồn
có trọng số:

```text
FutureConflictImpact = Σ_{c ∈ newConflictsInHorizon} weight(c) × Difficulty_estimate(c)
```

Đây **chỉ là heuristic để xếp hạng các nhánh cùng tầng** trong beam search — việc thực sự giải các xung đột
tương lai đó xảy ra ở các tầng sâu hơn của cùng cây beam search (đó mới là nguồn tối ưu đa bước thật sự,
không phải bản thân công thức Cost). Nói cách khác: beam search cung cấp "nhìn xa" thực sự bằng cách thực
sự mô phỏng tiếp; `FutureConflictImpact` chỉ giúp tỉa nhánh sớm và hợp lý hơn tại mỗi tầng.

---

## 11. Cyclic timetable — bài toán tuần hoàn thực sự, không phải lập lịch nhiều ngày (mục 21 — câu hỏi 15)
**— viết lại toàn bộ theo phản hồi.**

### 11.1 Phát biểu lại (nhắc lại mục 0.1)

Đây **không phải** bài toán "lập lịch cho Day-1, Day0, Day+1 rồi lấy Day0 làm kết quả". Đó là một cách
đọc sai dẫn tới nguy cơ coi mỗi ngày là một bài toán gần-độc-lập. Đúng bản chất: có **một** hàm lịch trình
canonical duy nhất cho mỗi `TrainService`,

```text
Schedule(Service, Station, n) = Schedule(Service, Station, 0) + n × 1440,   n ∈ ℤ
```

và mục tiêu là tìm `Schedule(·, ·, 0)` sao cho khi mở rộng tuần hoàn ra **toàn bộ trục thời gian vô hạn**
(mọi `n ∈ ℤ`, cho mọi service), không có ràng buộc cứng nào — kể cả no-conflict giữa hai `TrainInstance`
bất kỳ ở hai `CycleIndex` bất kỳ — bị vi phạm. `TrainInstance(Service, n)` chỉ là cách gọi tên cho
"nghiệm tại offset `n×1440`" — nó không phải một biến quyết định độc lập (mục 0.1).

### 11.2 Vì sao chỉ cần một cửa sổ hữu hạn `K` để kiểm chứng một điều kiện trên tập vô hạn `n ∈ ℤ`

Đây là phần cần chứng minh chặt, không chỉ "đề xuất":

Với hai `TrainService` bất kỳ `i, j` (cho phép `i = j`), do tính bất biến theo phép dịch của
`Schedule`, xung đột giữa `Instance(i, n)` và `Instance(j, m)` **chỉ phụ thuộc vào hiệu số**
`d = m - n`, không phụ thuộc bản thân `n`:

```text
Conflict(Instance(i,n), Instance(j,m))  ⟺  Conflict(Instance(i,0), Instance(j, m-n))
```

(vì dịch cả hai instance đi `-n×1440` không đổi việc chúng có chồng lấp thời gian hay không). Do đó, điều
kiện "không xung đột với mọi `n, m ∈ ℤ`" tương đương "không xung đột với mọi `d ∈ ℤ`, so với instance gốc
`(i,0)`" — đây là bước rút gọn từ 2 biến tự do (`n, m`) còn 1 biến tự do (`d`), nhưng `d` vẫn chạy trên
toàn bộ `ℤ`.

**Bước rút gọn thứ hai — chặn `d` bằng hình học của các khoảng chiếm dụng:** `Instance(i,0)` chiếm khoảng
thời gian tuyệt đối `[dep_i, dep_i + J_i]` (với `dep_i = FixedDepartureTimeOfDay(i) ∈ [0,1440)`,
`J_i = JourneyTime(i)`); `Instance(j,d)` chiếm `[dep_j + 1440d, dep_j + 1440d + J_j]`. Hai khoảng này
**chỉ có thể** giao nhau (điều kiện cần, chưa cần đủ, đã là quá đủ để chặn `d`) nếu:

```text
dep_i < dep_j + 1440d + J_j     và     dep_j + 1440d < dep_i + J_i

⟺  (dep_i - dep_j - J_j) / 1440  <  d  <  (dep_i - dep_j + J_i) / 1440
```

Vì `dep_i, dep_j ∈ [0, 1440)` nên `dep_i − dep_j ∈ (−1440, 1440)`. Đặt
`J_max = max(J_i, J_j) ≤ MaxJourneyTimeOverAllServices`, khoảng trên nằm gọn trong:

```text
d ∈ ( −1 − J_max/1440 ,  1 + J_max/1440 )
```

Do đó **mọi** `d` có khả năng gây xung đột đều thoả `|d| ≤ K` với:

```text
K := 1 + ceil( MaxJourneyTimeOverAllServices / 1440 )
```

— đây là một **cận trên chứng minh được** (không phải ước lượng kinh nghiệm): với `|d| > K`, hai khoảng
chiếm dụng tuyệt đối không thể giao nhau bất kể `dep_i, dep_j` cụ thể là bao nhiêu, nên chắc chắn không có
xung đột và không cần kiểm tra. Có thể cộng thêm `SafetyMargin` (config, mặc định 0) cho các trường hợp
biên do làm tròn/độ trễ headway cộng thêm ở sát mép khoảng.

**Kết luận (phần `i ≠ j`):** kiểm tra toàn bộ cặp `(i, j, d)` với `i ≠ j` và `d ∈ [-K, K]` — dùng instance
`(i, 0)` làm "đầu dò" cố định, so với `(j, d)` — là **đủ và cần thiết** để đảm bảo tuần hoàn vô hạn hợp lệ
giữa hai `TrainService` khác nhau. `K` ở đây là **một cận trên triển khai (implementation bound)** để xác
định các *relative cycle offset* cần đưa vào kiểm tra cho một bài toán tuần hoàn — **không phải** việc
solver đang lập lịch cho nhiều ngày độc lập rồi ghép lại; bản thân biến quyết định vẫn chỉ có đúng 1 bộ,
thuộc `TrainService` tại chu kỳ 0 (mục 0.1).

**Trường hợp `i = j` (một service so với chính bản sao chu kỳ khác của nó) — sửa lại so với bản trước:**
Bản Phase 1 trước đó đưa `i = j` vào diện phải kiểm tra chỉ dựa trên `JourneyTime > 1440`, và coi đây là
nguồn self-conflict. **Điều này sai và đã được sửa.** Lý do:

Trên mạng lưới **tuyến tính, mỗi khu gian được một `TrainService` đi qua đúng một lần trong một chu kỳ**,
occupation của service tại cùng một `Section` giữa hai chu kỳ liên tiếp thoả:

```text
Occupation(Service, n, Section) = Occupation(Service, 0, Section) + n × 1440
```

Độ rộng một occupation (= running time của đúng 1 khu gian, tính bằng phút, luôn nhỏ hơn nhiều so với
1440) không thể đủ lớn để hai occupation của cùng service, cùng section, ở hai chu kỳ liên tiếp
(`n` và `n+1`, cách nhau đúng 1440 phút) giao nhau. Việc `JourneyTime(Service) > 1440` chỉ có nghĩa là
**nhiều instance của cùng service cùng tồn tại đồng thời trên các vị trí khác nhau của tuyến** (ví dụ
SE1/0 đang ở ga 140 trong khi SE1/+1 mới xuất phát ở ga 1) — đây là điều kiện làm cho `K` cần `> 1` để bắt
đủ các cặp **khác service** đang cùng lúc hiện diện trên tuyến, chứ **không tự nó tạo ra xung đột giữa
hai instance của cùng một service tại cùng một resource**. Vì vậy:

```text
Với network tuyến tính hiện tại: LOẠI TRỪ i = j khỏi tập cặp (i,j,d) cần ConflictDetector kiểm tra.
```

**Giữ lại trong kiến trúc (không xoá code path) cho các trường hợp tương lai** mà self-conflict `i=j` là
có thật: route dạng vòng (một ga/khu gian được service đi qua nhiều lần trong 1 chu kỳ), hoặc occupation
của một resource kéo dài xấp xỉ/hơn 1440 phút (không xảy ra với occupation cấp khu gian trong bài toán
hiện tại, nhưng có thể xảy ra nếu sau này mô hình hoá occupation ở cấp độ khác, vd. chiếm dụng cả 1 depot
nhiều giờ). `ConflictDetector` nên nhận một cờ cấu hình `IncludeSelfServiceConflicts: bool` (mặc định
`false` cho tuyến HN–SG tuyến tính hiện tại) thay vì hard-code loại trừ `i=j`, để bật lại không cần đổi
kiến trúc khi mở rộng mạng lưới.

### 11.3 Thuật toán detection & validate

```text
ConflictDetector.Detect(services):
    occupations := []
    for service in services:
        for n in [-K, K]:                       // K tính theo mục 11.2, một lần cho toàn hệ thống
            occupations += SectionOccupation.From(service.Trajectory, cycleIndex = n)
    → chạy sweep-line (mục 5) như bình thường trên TOÀN BỘ occupations này
    → chỉ giữ lại Conflict mà:
        (a) ÍT NHẤT MỘT bên có CycleIndex == 0   (lập luận 11.2: cặp còn lại là suy ra được/trùng lặp
                                                    với một cặp đã có CycleIndex 0)
        (b) hai bên thuộc HAI TrainService KHÁC NHAU (ServiceId khác nhau)   — mặc định
            (IncludeSelfServiceConflicts = false, xem giải thích ở trên) trên network tuyến tính hiện tại;
            khi RESOLVE, ghi decision vào TrainService phía có mặt trong cặp, bất kể cặp đó lấy
            "đầu dò" là bên nào.
```

Vì đây là cách `ConflictDetector` hoạt động **mặc định** (không phải một chế độ đặc biệt bật lên sau), nó
áp dụng xuyên suốt từ Phase 3 (detection thuần) tới Phase 7 (trong vòng lặp beam search, mục 9.2) — không
có khái niệm "phát hiện trong ngày" tách biệt với "phát hiện xuyên biên chu kỳ".

```text
ValidateCyclicBoundary()  (Phase 9, TimetableValidator — độc lập với solver):
    Chạy lại đúng thuật toán Detect ở trên từ đầu trên nghiệm cuối cùng (Schedule(·,·,0) đã solver chốt),
    với K tính lại (không tin cache của solver), xác nhận danh sách Conflict trả về RỖNG.
    Council thêm: kiểm tra riêng các occupation có ExitTime hoặc EntryTime rơi vào [-margin, margin] và
    [1440-margin, 1440+margin] (sát mép chu kỳ) để dễ debug khi có vi phạm — dù về logic, thuật toán trên
    đã bao trùm toàn bộ trục, không chỉ vùng sát mép.
```

Nghiệm cuối cùng công bố = `Schedule(·,·,0)` của mọi `TrainService` (đúng theo yêu cầu đề bài); các
instance `n≠0` không bao giờ được lưu, chỉ tồn tại tạm thời trong bộ nhớ lúc detect/validate.

---

## 12. Các nguyên nhân có thể khiến toàn bộ timetable infeasible (câu hỏi 16)

1. `TotalBuffer(T) < 0` cho ít nhất 1 `TrainService` — bản thân giờ đi/đến cố định đã không tự nhất quán
   với `MinimumJourneyTime` (chưa cần xét service khác).
2. Tại một xung đột, **không candidate station nào trong cửa sổ** thoả `CanMeet`/`CanOvertake` — cần mở
   rộng `CandidateWindow` (cấu hình) hoặc đây là giới hạn hạ tầng thật (khu gian quá dài không có ga tránh).
3. Với mọi candidate khả dĩ, `RequiredShift > ForwardSlack` (mục 4.1) của cả hai service — không đủ quỹ
   thời gian cục bộ để giải dù ở bất kỳ ga nào trong cửa sổ (kể cả sau khi Phase 8 thử mượn
   `RedistributableSlack` theo mục 4.2 mà vẫn không đủ, hoặc việc mượn bị chặn vì re-validate upstream
   thất bại).
4. **Hiệu ứng dây chuyền**: giải xung đột A tiêu hết slack cần cho xung đột B ngay sau đó, và không nhánh
   beam search nào (trong `BeamWidth` × `LookAheadConflicts` đã thử) sống sót — biểu hiện bằng
   `frontier` rỗng sau một tầng.
5. **Giới hạn năng lực ga**: nhiều hơn `MaxSimultaneousTrains` của một ga cần đỗ/tránh cùng lúc tại cùng
   thời điểm (ví dụ 3 service cùng cần gặp nhau ở ga chỉ có 2 track).
6. ~~Một `TrainService` tự xung đột với chính nó qua chu kỳ~~ — **đã loại bỏ khỏi danh sách nguyên nhân
   sau khi sửa mục 11.2**: trên network tuyến tính hiện tại, occupation của cùng một service tại cùng một
   `Section` giữa hai chu kỳ liên tiếp cách nhau đúng 1440 phút, trong khi độ rộng occupation (= running
   time 1 khu gian) luôn nhỏ hơn nhiều 1440 → không thể tự giao nhau. `JourneyTime > 1440` chỉ tạo ra hiện
   tượng nhiều instance của cùng service cùng hiện diện trên tuyến, làm `K` (mục 11.2) cần `>1` để bắt đủ
   xung đột **giữa các service khác nhau**, chứ không phải nguồn tự-xung-đột. Giữ hook kiến trúc
   (`IncludeSelfServiceConflicts`) cho tương lai (route vòng, resource chiếm dụng dài).
7. **Vi phạm chu kỳ giữa hai service khác nhau không thể sửa**: sau khi quét đủ `K` theo mục 11.2, phát
   hiện một cặp `(Service_i, Service_j, d)` xung đột mà không còn `ForwardSlack` (và cả
   `RedistributableSlack` đã re-validate) để điều chỉnh, vì `FixedDepartureTimeOfDay` mỗi service là bất
   biến qua mọi chu kỳ.
8. Mâu thuẫn ưu tiên/priority tuyệt đối kết hợp ràng buộc cứng khác (ví dụ 2 service cùng "không được dừng
   ở bất kỳ đâu ngoài ga đã khai báo" và không có ga hợp lệ nào trong vùng giao nhau).

`TimetableValidator` (Phase 9, độc lập với solver — mục 24) phải phát hiện và **định vị chính xác** loại
nào trong 8 loại trên đang xảy ra, trả về `{Service, Station/Section, Constraint, Expected, Actual}` thay
vì chỉ báo "infeasible".

---

## 13. Bộ Unit Test đề xuất trước khi code solver (câu hỏi 17)

Giữ đúng 10 kịch bản đề bài đã liệt kê ở mục 28, ánh xạ rõ module nào được kiểm chứng — dùng làm checklist
nghiệm thu cho từng Phase:

| # | Kịch bản | Module chính được test | Phase |
|---|----------|------------------------|-------|
| 1 | 3 ga, 2 tàu ngược chiều, 1 xung đột | CandidateGenerator + MeetResolver chọn đúng ga | 6 |
| 2 | 5 ga, 2 tàu, buffer khác nhau | CandidateEvaluator không chọn theo RequiredShift nhỏ nhất một cách mù quáng khi BufferConsumptionRatio lớn | 6 |
| 3 | Tàu vốn dừng tại ga tránh vs. tàu phải ForcedStop | RequiredShiftCalculator — ưu tiên ga tàu đã dừng sẵn khi điều kiện khác tương đương | 5 |
| 4 | 2 xung đột liên tiếp | Chứng minh pure-greedy cho nghiệm xấu; Beam Search cho nghiệm tốt hơn | 7 |
| 5 | Tàu nhanh đuổi tàu chậm | OvertakeResolver + phát hiện OVERTAKE | 3, 6 |
| 6 | Xung đột qua biên 23:xx / 00:xx giữa 2 service khác nhau | ConflictDetector xét đúng cặp `(i,j,d=1)` theo mục 11.2 | 3, 9 |
| 7 | Tàu không đủ TotalBuffer | BufferCalculator báo infeasible ngay từ đầu, không đưa vào solver | 2 |
| 8 | Technical stop 20 phút | MinimumTimetableBuilder + StopRules | 2 |
| 9 | Passenger stop 3 phút | MinimumTimetableBuilder + StopRules | 2 |
| 10 | ForcedStop sinh +1/+2 phút | RunningTimeRules áp dụng đúng khi StopType chuyển từ Through → ForcedMeet/ForcedOvertake | 5 |

Bổ sung thêm (phát sinh từ phân tích ở trên, nên có thêm trước khi vào Phase 7):

| # | Kịch bản | Lý do thêm |
|---|----------|------------|
| 11 | Hành trình dài hơn 24h (vd 30h), 2 service KHÁC NHAU cùng hiện diện trên tuyến | Kiểm chứng `K > 1` (mục 11.2) bắt đúng xung đột `(i≠j, d=1)`; đồng thời khẳng định KHÔNG có false-positive self-conflict `(i=j, d≠0)` khi chỉ có 1 service chạy hàng ngày (đúng bản sửa mục 11.2/12) |
| 12 | Ga chỉ có `CanMeet=true` nhưng `CanOvertake=false` | CandidateGenerator lọc đúng theo `Conflict.Type` |
| 13 | 3 service cùng cần gặp nhau tại 1 ga chỉ có 2 track | Phát hiện đúng nguyên nhân infeasible #5 (mục 12) |
| 14 | Reallocate buffer sau khi có nghiệm khả thi (Phase 8) | `SlackReallocationStrategy` (mục 4.2) cải thiện phân bố recovery, có re-validate, không phá vỡ nghiệm đã đúng |
| 15 | Resolve 1 conflict giữa `Instance(A,0)` và `Instance(B,1)` | Quyết định phải ghi vào `TrainService B` (chu kỳ 0) — sau khi apply, `Instance(B,0)` và `Instance(B,1)` đều dịch đúng cùng lượng, không được sửa lệch nhau (mục 0.1 hệ quả 2) |
| 16 | Resolve xong 1 conflict làm dịch `TrainService W`, việc dịch chuyển đó lại tạo xung đột mới giữa `Instance(W, d+1)` (trước đó vô hại) và 1 service thứ 3 | `RecalculateAffected` phải quét lại TOÀN BỘ `CycleIndex ∈ [-K,K]` của `W`, không chỉ cặp instance ban đầu (mục 0.1 hệ quả 3, mục 9.2) |
| 17 | Bộ 10 scenario Section Release Headway (mục 5.7): cùng/ngược chiều × gap {3,2,0,overlap}, cyclic, double-track | `ConflictDetector` áp đúng MỘT rule gap chung cho cả MEET/HEADWAY (mục 5.2), đặc biệt biên `ActualGap==0` vẫn là `Conflict` — sai sót dễ mắc nhất nếu lầm tưởng đây là bài toán "interval overlap" thuần túy |

---

## 14. Quyết định kỹ thuật đã chốt & câu hỏi còn mở trước khi vào Phase 2

### 14.1 Đã chốt

- **Ngôn ngữ/stack: C# / .NET 8 (LTS)**, unit test bằng **xUnit**.
- `Domain` và `Engine` (namespace theo mục "kiến trúc code" của đặc tả gốc) **độc lập với UI/database** —
  không phụ thuộc EF Core, ASP.NET, hay bất kỳ package hạ tầng nào; chỉ POCO/record + interface.
- Phase 2 dùng **in-memory data** (danh sách `Station`/`Section`/`TrainService` khởi tạo trực tiếp trong
  code hoặc test fixture); chưa tích hợp SQL Server — việc đọc dữ liệu từ DB thật là một
  `IRepository`/mapping layer làm sau, không ảnh hưởng `Domain`/`Engine`.
- Toàn bộ hằng số nghiệp vụ (`PassengerStop`, `TechnicalStop`, `AccelerationPenalty`,
  `DecelerationPenalty`, `SectionReleaseHeadwayMinutes` — mục 5.6, dùng chung MEET/HEADWAY,
  `OvertakeHeadway`, `CandidateWindow`, `BeamWidth`, `LookAheadConflicts`, các trọng số cost `α..ε`) nằm
  trong `Configuration`, truyền vào
  `Engine` qua interface (`IStopRules`, `IRunningTimeRules`, `IHeadwayRules`, `SolverParameters`,
  `CostWeights`) — không hard-code ở bất kỳ đâu trong `Engine`.
- Có thể dùng **immutable record** cho `Domain` (Station, Section, TrainService, TimetableEntry...) và mô
  hình **persistent/delta state** (mục 9.1) cho `SearchState` của beam search — implement bằng C# `record`
  (value equality + `with`-expression cho copy-on-write nhẹ) kết hợp con trỏ `ParentState` để giữ tính
  chất O(1)-theo-số-tàu-bị-ảnh-hưởng đã phân tích ở mục 9.1.

### 14.2 Còn mở — cần xác nhận trước khi viết code Phase 2

1. **Nguồn dữ liệu 178 ga & khu gian** — đã có sẵn (CSV/DB/API) hay cần mock cho Phase 2–7 rồi nạp dữ
   liệu thật sau?
2. **Dữ liệu năng lực tránh/vượt từng ga** (`StationTrack`) — đã có, hay tạm thời giả định `CanMeet=true`
   cho tất cả ga và bổ sung sau?
3. ~~**`SameDirectionHeadway` mặc định**~~ — **ĐÃ CHỐT** (2026-08-23, trước khi code Phase 3, mục 5.2/5.6):
   không phải một hằng số riêng "tạm dùng chung" — đúng nghiệp vụ, giãn cách cùng chiều và ngược chiều
   trên cùng Section **là cùng một quy tắc** (Section Release Headway = 3 phút, tính từ
   `Later.EntryTime − Earlier.ExitTime`), gộp thành `HeadwayRules.SectionReleaseHeadwayMinutes` duy
   nhất — không còn 2 hằng số `MeetHeadway`/`SameDirectionHeadway` tách biệt nữa.

Đề xuất: mock dữ liệu ga/khu gian tối thiểu cho Phase 2–7 (đủ để chạy 16 unit test ở mục 13), dùng
`SectionReleaseHeadwayMinutes = 3` (mục 5.6) làm giá trị mặc định duy nhất cho cả MEET và HEADWAY, và bắt
đầu code Phase 2
(solution `.sln` + project `Domain`, `Engine`, `Configuration`, và `*.Tests` bằng xUnit) ngay khi được xác
nhận.

---

## 15. Phase 2.5 — Import dữ liệu hành trình thực tế vào Domain (mapping layer)

Trả lời câu hỏi mở "nguồn dữ liệu" ở mục 14.1: **không dùng bảng `SectionRunningTime` riêng trong
database** — map trực tiếp từ bảng "hành trình" hiện có (mỗi dòng = 1 ga trong hành trình của 1 tàu) sang
`Domain` (mục 1). Đây là một **mapping layer** thuần túy (giống ghi chú ở mục 14.1: *"đọc dữ liệu từ DB
thật là một `IRepository`/mapping layer làm sau, không ảnh hưởng `Domain`/`Engine`"*), không phải bảng
DB mới, không phải logic solver.

> **Sửa lại (2026-08-23, sau khi xác nhận đúng cấu trúc dữ liệu thật):** bản nháp đầu của mục này giả
> định sai rằng Arrival/Departure tại các ga TRUNG GIAN là input có sẵn trong raw data, và từ đó định
> tái tạo "existing scheduled slack/recovery" bằng cách so sánh raw schedule với minimum trajectory. Cả
> hai giả định đó đều **sai** và đã bị loại bỏ hoàn toàn khỏi mục 15. Đúng ra: **input cố định theo thời
> gian chỉ có ở đúng 2 điểm** (giờ đi ga xuất phát, giờ+ngày đến ga cuối) — Arrival/Departure ở MỌI ga
> trung gian là **OUTPUT** do `MinimumTimetableBuilder` (và sau này solver) tính ra, không phải đọc từ
> DB. Toàn bộ mục 15 dưới đây viết lại theo đúng mô hình input/output này.

### 15.1 Raw input row — `TimetableSourceRow` (đã sửa: khớp đúng schema SQL thật)

> **Sửa lại (2026-08-23, đã xác nhận schema SQL thật):** bản nháp trước tự đặt tên cột
> (`FixedDepartureTimeOfDayMinutes`, `FixedArrivalTimeOfDayMinutes`) và giả định `StationCode: string`,
> đồng thời mô tả sai rằng các cột Arrival/Departure "không tồn tại" trên dòng trung gian. Thực tế:
> bảng chỉ có **một schema cột duy nhất** dùng chung cho mọi dòng (kể cả dòng trung gian) —
> `ArrivalTime`/`ArrivalDayNumber`/`DepartureTime`/`DepartureDayNumber` **là cột có thật trong DB ở MỌI
> dòng**, chỉ khác nhau ở **vai trò dùng cột đó** tuỳ vị trí dòng. Bảng dưới đây map lại đúng tên cột SQL
> (không tự đặt tên khác) và ghi rõ vai trò từng cột theo vị trí dòng.

Schema SQL thật:

```text
TrainCode                        varchar(50)
JourneySequence                  int
StationCode                      int              -- SỐ, không phải chuỗi
ArrivalTime                      time(7)          -- time-of-day, có phần thập phân giây (không dùng)
ArrivalDayNumber                 int
DepartureTime                    time(7)
DepartureDayNumber               int
MinimumRunningTimeToNextStation  int
PassengerStopMinutes             int
TechnicalStopMinutes             int
```

`TimetableSourceRow` (kiểu CLR map 1:1 theo cột, dùng `TimeSpan?` cho `time(7)` — SQL `time(7)` là
time-of-day thuần túy, KHÔNG có phần "ngày", nên khi đọc phải cắt về phút nguyên:
`TimeOfDayMinutes = (int)Math.Truncate(value.TotalMinutes)`, bỏ hẳn phần giây/dưới-giây; nếu dữ liệu thật
có phần giây khác 0 thì đây là mất mát có chủ đích — domain chỉ làm việc ở độ phân giải phút, mục 2):

```text
TimetableSourceRow
  TrainCode: string
  JourneySequence: int              // 1..N — thứ tự ga trong hành trình CỦA CHÍNH tàu này
  StationCode: int                  // KHÔNG phải string — số hiệu ga trong DB
  ArrivalTime: TimeSpan?
  ArrivalDayNumber: int?
  DepartureTime: TimeSpan?
  DepartureDayNumber: int?
  MinimumRunningTimeToNextStation: int?  // NULL/0 ở dòng cuối (không có khu gian kế tiếp)
  PassengerStopMinutes: int
  TechnicalStopMinutes: int
```

**Vai trò 4 cột thời gian theo vị trí dòng — đây là điểm mấu chốt cần map đúng:**

```text
Dòng JourneySequence = 1  (ga xuất phát):
    DepartureTime, DepartureDayNumber   → INPUT (đọc, dùng làm FixedDepartureTimeOfDayMinutes)
    ArrivalTime, ArrivalDayNumber       → không có ý nghĩa nghiệp vụ (ga xuất phát không "đến"); ignore

Dòng JourneySequence = N  (ga đích):
    ArrivalTime, ArrivalDayNumber       → INPUT (đọc, dùng làm FixedArrivalAbsoluteMinutes)
    DepartureTime, DepartureDayNumber   → không có ý nghĩa nghiệp vụ (ga đích không "đi tiếp"); ignore

Dòng 1 < JourneySequence < N  (MỌI ga trung gian):
    ArrivalTime, ArrivalDayNumber, DepartureTime, DepartureDayNumber
        → CẢ 4 CỘT ĐỀU LÀ "IgnoredAsInput / SolverOutput", KHÔNG PHẢI "không tồn tại"
        → cột CÓ THỂ có giá trị sẵn trong DB (vd. từ lần chạy solver trước, hoặc placeholder nhập tay),
          nhưng adapter khi LOAD để đưa vào MinimumTimetableBuilder/solver PHẢI BỎ QUA hoàn toàn giá
          trị hiện có ở 4 cột này tại các dòng trung gian — không đọc, không dùng để validate, không
          suy luận gì từ chúng. Khi SAVE kết quả (mục 15.9), adapter PHẢI GHI ĐÈ (overwrite) 4 cột này
          bằng giá trị solver vừa tính ra, bất kể giá trị cũ là gì.
```

**Validate bắt buộc:** `DepartureDayNumber` tại dòng `JourneySequence=1` phải **đúng bằng 0** (quy ước
Day 0 = ngày xuất phát, đã xác nhận — mục 15.3) — nếu khác 0, đây là dữ liệu nguồn sai hoặc quy ước ngày
không khớp giả định, phải báo lỗi định vị chính xác (`TrainCode`, giá trị thực tế), không tự "chuẩn hoá"
âm thầm bằng cách trừ đi cho khớp.

Vẫn giữ đúng như đã xác nhận trước đó: mỗi hành trình có dòng cho **mọi ga vật lý** dọc tuyến kể cả ga
chạy thông, nên `MinimumRunningTimeToNextStation` của dòng `i` luôn ứng đúng **một** `Section` liền kề
trong `RailwayNetwork` (không phải tổng gộp qua nhiều khu gian).

### 15.2 Group & order

- Group theo `TrainCode` — mỗi group = đúng **một** `TrainService` (canonical pattern, mục 0.1/1.4);
  bảng hành trình hiện tại không có khái niệm "nhiều lượt chạy" riêng biệt cần phân biệt thêm.
- Sắp dòng trong group theo `JourneySequence` tăng dần → `r[1..N]`. `OriginStation = r_1`,
  `DestinationStation = r_N`.
- Kiểm tra chất lượng dữ liệu bắt buộc: `JourneySequence` của một group phải là dãy liên tục `1..N`,
  không thiếu/không lặp; `r_1.DepartureTime`/`r_1.DepartureDayNumber` phải có giá trị (`DepartureDayNumber
  = 0`, xem mục 15.1); `r_N.ArrivalTime`/`r_N.ArrivalDayNumber` phải có giá trị — nếu vi phạm, loại
  `TrainCode` đó khỏi import và báo lỗi định vị chính xác, không âm thầm suy đoán hay bỏ qua.

### 15.3 Chuyển đổi Day/Time ⇄ absolute minutes (dùng cả hai chiều: đọc input và ghi output)

> **Sửa lại (quy ước Day — đã xác nhận với DB thật):** DB dùng **zero-based day number** —
> `DepartureDayNumber` tại ga xuất phát = **0** (Day 0 = ngày xuất phát, Day 1 = ngày kế tiếp, Day 2 =
> ngày thứ ba, ...), **không phải** Day 1 như bản nháp trước. Quy ước này thực ra trùng khớp 100% với
> quy ước **đã có sẵn** trong `Domain` từ mục 2 (dòng ~304: *"tuyệt đối so với mốc `Day0 00:00 = 0`"*) —
> nên Phase 2.5 không cần một quy ước "nội bộ" riêng nữa, chỉ cần dùng **đúng** quy ước Day0 đã có.

Chọn **mốc 0 tuyệt đối = 00:00 của Day 0** (ngày của `r_1`, ga xuất phát) — trùng khớp với cách
`TrainService.FixedDepartureTimeOfDayMinutes`/`JourneyTimeMinutes` và toàn bộ
`TimetableEntry.ArrivalTimeMinutes`/`DepartureTimeMinutes` hiện tại (mục 1.4, 1.5, mục 2) đã biểu diễn
thời gian: **số phút tuyệt đối kể từ đầu Day 0, không mod 1440** — nên **không cần thêm kiểu dữ liệu
mới** trong `Domain`, chỉ cần đúng công thức chuyển đổi ở 2 biên (đọc input / ghi output).

**Chiều vào (input → absolute minutes), chỉ dùng 2 dòng đầu/cuối — đọc đúng tên cột SQL (mục 15.1):**

```text
// Bước 0 (bắt buộc): validate r_1.DepartureDayNumber == 0 (mục 15.1) TRƯỚC khi tính gì —
// nếu sai, dừng import cho TrainCode này, không tự "trừ bù" cho khớp.

TimeOfDayMinutes(t: TimeSpan) = (int) Math.Truncate(t.TotalMinutes)   // cắt bỏ giây/dưới-giây (mục 15.1)

FixedDepartureTimeOfDayMinutes  = TimeOfDayMinutes(r_1.DepartureTime)          // ∈ [0,1439]
                                 // r_1.DepartureDayNumber đã validate = 0 nên không cần cộng vào
FixedArrivalAbsoluteMinutes     = r_N.ArrivalDayNumber × 1440
                                 + TimeOfDayMinutes(r_N.ArrivalTime)
                                 // KHÔNG trừ 1 — Day 0 CHÍNH LÀ mốc gốc, không phải Day 1
JourneyTimeMinutes              = FixedArrivalAbsoluteMinutes − FixedDepartureTimeOfDayMinutes
                                 // KHÔNG modulo 1440 — đúng tinh thần mục 0 (hành trình có thể > 24h)
```

**Ví dụ số (đúng theo ví dụ đã xác nhận):**

```text
Departure 22:00 (= 22×60 = 1320 phút-trong-ngày), Day 0
    → FixedDepartureTimeOfDayMinutes = 0 × 1440 + 1320 = 1320

Arrival 05:00 (= 5×60 = 300 phút-trong-ngày), Day 2
    → FixedArrivalAbsoluteMinutes    = 2 × 1440 + 300   = 3180

JourneyTimeMinutes = 3180 − 1320 = 1860 phút
```

**Chiều ra (absolute minutes → Day/Time), dùng khi xuất output — mục 15.9:**

```text
DayNumber (quy ước Day 0)  = floor(AbsoluteMinutes ÷ 1440)
TimeOfDayMinutes           = positiveModulo(AbsoluteMinutes, 1440)

positiveModulo(x, m) = ((x mod m) + m) mod m     // ĐÚNG cho cả x âm — toán tử `%` trần trong C#
                                                  // (và nhiều ngôn ngữ khác) trả về số dư ÂM khi x âm,
                                                  // KHÔNG phải phép modulo toán học ở trên
```

Hai chiều này là nghịch đảo của nhau chính xác vì cùng dùng một mốc 0 (đầu Day 0) — không có sai số quy
đổi tích lũy khi đi qua nhiều Section như một cách làm "cộng dồn Day rồi mod" theo từng bước dễ mắc lỗi.

**Vì sao helper phải xử lý đúng cả số âm dù `TrainService` (chu kỳ 0) luôn có absolute time ≥ 0:**
`FixedDepartureTimeOfDayMinutes ∈ [0, 1440)` (mục 1.4) nên canonical trajectory của Phase 2.5 không bao
giờ sinh ra `AbsoluteMinutes` âm. Nhưng cùng helper `floor`/`positiveModulo` này rất có thể được tái
dùng để hiển thị/ghi log cho `TrainInstance(ServiceId, CycleIndex)` (mục 1.4, 11) — vốn được suy ra bằng
`Schedule(Service, Station, 0) + CycleIndex × 1440` với `CycleIndex` **có thể âm** (Day-1, Day-2... khi
xét cửa sổ cyclic quanh nửa đêm, mục 11). Dùng `floor`/`positiveModulo` đúng chuẩn toán học ngay từ đầu
tránh phải viết lại helper này khi Phase 3+ chạm tới `TrainInstance` âm chu kỳ.

### 15.4 Mapping ra `TrainService`

```text
TrainService:
    ServiceId                       = TrainCode        // 1 TrainCode ↔ 1 canonical pattern (hiện tại)
    TrainCode                       = TrainCode
    OriginStationSequence           = Sequence(r_1.StationCode)      // tra qua RailwayNetwork — mục 15.1
    DestinationStationSequence      = Sequence(r_N.StationCode)      // (StationCode giờ là int, mục 15.1)
    Direction                       = Inbound nếu OriginSeq < DestSeq, ngược lại Outbound
    FixedDepartureTimeOfDayMinutes  = TimeOfDayMinutes(r_1.DepartureTime)     // mục 15.3
    JourneyTimeMinutes              = FixedArrivalAbsoluteMinutes − FixedDepartureTimeOfDayMinutes  // mục 15.3
    Priority                        = CHƯA có nguồn trong bảng hành trình — xem mục 15.11
```

`TrainService` (mục 1.4) chỉ cần đúng 2 đầu mút này — **không** cần, và **không được** đọc bất kỳ giá trị
nào từ 4 cột Arrival/Departure ở các dòng trung gian, vì đó là `IgnoredAsInput / SolverOutput` (mục 15.1),
không phải input đáng tin cậy.

`Sequence(StationCode)` giả định có sẵn một lookup `Station` theo `StationCode` — gap thực tế của lookup
này (chưa tồn tại trong `RailwayNetwork` đã commit) được ghi ở mục 15.11.

### 15.5 Dwell time tại ga trung gian → `TrainStopRequirement` (KHÔNG có `ScheduledDwell` đầu vào)

Với mỗi `r_i`, `1 < i < N` (bỏ qua `r_1`/`r_N` — ga xuất phát/đích đã LUÔN "dừng" theo đúng logic
`willStop` hiện tại của `MinimumTimetableBuilder`, xem `MinimumTimetableBuilder.cs` dòng ~50, nên không
cần khai `StopRequirement` riêng cho 2 ga này):

```text
RequiresPassengerStop = r_i.PassengerStopMinutes > 0
RequiresTechnicalStop = r_i.TechnicalStopMinutes  > 0

MandatoryStopDuration(r_i) =
    !RequiresPassengerStop && !RequiresTechnicalStop  → 0                              // chạy thông
    RequiresPassengerStop && RequiresTechnicalStop    → CombineMode(IStopRules) áp dụng
                                                          TRÊN CHÍNH 2 giá trị của dòng này:
                                                            Max → Max(r_i.PassengerStopMinutes,
                                                                      r_i.TechnicalStopMinutes)
                                                            Sum → r_i.PassengerStopMinutes
                                                                  + r_i.TechnicalStopMinutes
    chỉ 1 trong 2 đúng                                 → giá trị tương ứng của dòng này

StopDurationOverrideMinutes(r_i) = MandatoryStopDuration(r_i)   // truyền vào TrainStopRequirement
                                                                  làm override (mục 3.1, StopRules.cs)
```

**Khác với bản nháp trước:** `PassengerStopMinutes`/`TechnicalStopMinutes` của raw data **không phải**
một "dwell thực tế đã quan sát được" để copy thẳng — đây là **mandatory minimum minutes theo từng loại
tác nghiệp tại ga đó**, và phải đi qua đúng rule `CombineMode` (`Max`/`Sum`, cấu hình trong `IStopRules`,
mục 3.1) để ra `MandatoryStopDuration` — chỉ khác `StopRules.ResolveStopMinutes` hiện tại ở chỗ 2 giá trị
đưa vào combine là **của riêng dòng này** (do ga khác nhau có thể có mandatory minutes khác nhau), không
phải 2 hằng số cấu hình toàn cục (`IStopRules.PassengerStopMinutes`/`TechnicalStopMinutes` — 2 hằng số
đó giờ chỉ còn là **giá trị mặc định** dùng khi ga không có input cụ thể, ví dụ tàu mới thêm bằng tay).
Việc combine 2 giá trị *tường minh* (khác 2 hằng số toàn cục) theo `CombineMode` chưa có sẵn trong
`IStopRules` hiện tại (`ResolveStopMinutes` chỉ combine đúng 2 hằng số cố định của chính nó) — khi code
Phase 2.5 thật, cần thêm 1 method nhỏ kiểu `CombineExplicitStopMinutes(passengerMinutes, technicalMinutes,
combineMode)` để mapper và `StopRules` dùng chung logic Max/Sum, tránh lặp code (chưa code ở bước này).

### 15.6 Dựng minimum trajectory & biểu diễn buffer CHƯA phân bổ

```text
MinimumJourneyTime = Σ MinimumRunningTimeToNextStation(mọi khu gian, sau khi validate/dedupe — mục 15.7)
                    + Σ MandatoryStopDuration(mục 15.5)
                    + Σ AccelerationPenalty + Σ DecelerationPenalty        // đúng công thức mục 3

TotalBuffer = JourneyTimeMinutes (mục 15.3/15.4) − MinimumJourneyTime     // đúng BufferCalculator hiện có
```

Đây chính xác là những gì `MinimumTimetableBuilder` + `BufferCalculator` (Phase 2, **đã implement, không
đổi gì**) đã làm — `MinimumTimetableBuilder` build trajectory với `RecoveryTimeFromPrevMinutes = 0` ở
**mọi** entry (xem `MinimumTimetableBuilder.cs` dòng 101). Điều này giờ được xác nhận là **đúng mô
hình**, không phải giá trị khởi tạo tạm thời chờ cấy: **database không cho biết buffer thực tế đang nằm
ở đâu** (vì Arrival/Departure trung gian không phải input — mục 15.1), nên **không có cơ sở nào để suy
luận một cách phân bổ recovery ban đầu khác 0**. Hệ quả biểu diễn trong `Domain`:

- `TotalBuffer` (một số vô hướng, từ `BufferCalculator.Calculate`) là **quỹ chưa phân bổ**, không gắn với
  bất kỳ `Section`/`TimetableEntry` cụ thể nào.
- Trên trajectory tối thiểu, quỹ này biểu diễn **ngầm định** dưới dạng khoảng cách giữa
  `MinimumTrajectory.Last.ArrivalTimeMinutes` và `TrainService.FixedArrivalTimeMinutes` — không có
  trường nào trên `TimetableEntry` lưu trực tiếp con số "buffer còn lại tính tới đây"; muốn biết, luôn
  phải hỏi `BufferCalculator.ComputeForwardSlackMinutes(service, trajectory, k)` (mục 4.1), không đọc
  trực tiếp field nào.
- `RecoveryTimeFromPrevMinutes = 0` khắp nơi trên trajectory tối thiểu là **baseline đúng và duy nhất**
  suy ra được từ raw data — mọi cách "rải" khác (đều, theo heuristic, theo lịch sử xung đột...) đều là
  một quyết định của `BufferAllocator` cần **thêm thông tin ngoài bảng hành trình** (xem mục 15.9), Phase
  2.5 (mapping layer thuần túy) không tự ý làm việc đó.

### 15.7 Validate & dedupe `MinimumRunningTimeToNextStation` → `Section` (yêu cầu chính, không đổi so với bản trước)

Với mọi cặp liên tiếp `(r_i, r_{i+1})` của **mọi** `TrainCode`, gom thành 1 quan sát:

```text
(SectionKey = (min(FromSeq,ToSeq), max(FromSeq,ToSeq)), Direction, TrainCode, MinRunningTime)
```

rồi group theo `(SectionKey, Direction)`:

```text
foreach group in observations.GroupBy(SectionKey, Direction):
    distinctValues := group.Select(MinRunningTime).Distinct()
    if distinctValues.Count == 1:
        # NHẤT QUÁN giữa mọi tàu cùng khu gian + cùng chiều → dedupe an toàn
        Section.MinRunningTimeMinutes[Direction] := distinctValues.Single()
    else:
        # KHÔNG được ép thành 1 giá trị chung (yêu cầu tường minh) — xem mục 15.8
        RecordNonUniform(SectionKey, Direction, group.ToDictionary(TrainCode, MinRunningTime))
```

Kết quả của bước này là một **báo cáo** (`SectionRunningTimeValidationReport`): danh sách
`(SectionId, Direction)` nhất quán (đã dedupe xong, không cần làm gì thêm) và danh sách
`(SectionId, Direction)` không nhất quán kèm bảng `TrainCode → giá trị khác nhau`. Phase 2.5 **không tự
quyết định** giá trị nào "đúng" — chỉ báo cáo trung thực để người vận hành xác nhận đây là khác biệt hợp
lệ (vd. đầu máy/loại tàu khác nhau chạy cùng khu gian với tốc độ kỹ thuật khác nhau) hay là lỗi nhập liệu.

### 15.8 Thay đổi tối thiểu cho Domain/Engine khi có Section non-uniform (chỉ code KHI thực sự phát sinh)

Nếu bước 15.7 cho thấy **mọi** `(Section, Direction)` đều nhất quán (khả năng cao với dữ liệu kỹ thuật
thật, vì cùng loại đầu máy/đoàn tàu chạy cùng khu gian vật lý thường có cùng min running time) →
**không cần đổi gì** trong `Domain`/`Engine` hiện tại: chỉ build thẳng `Section.MinRunningTimeMinutes`
(kiểu dữ liệu đã có sẵn) từ kết quả dedupe, `MinimumTimetableBuilder`/`RailwayNetwork` chạy y nguyên như
Phase 2 đã commit.

Nếu phát sinh **ít nhất một** `(Section, Direction)` non-uniform → cần tách biệt running-time ở mức
`Section + Direction + TrainService/TrainClass`, nhưng **không sửa `Section`** (giữ `Section` là network
topology thuần túy, dùng chung cho mọi tàu — đúng tinh thần mục 1.2). Thay vào đó thêm một lớp resolve
nhỏ ở tầng `Engine`:

```text
ISectionRunningTimeResolver
    GetMinRunningTimeMinutes(Section section, Direction direction, TrainService service) : int
        // Ưu tiên: override riêng theo TrainCode (hoặc TrainClass nếu sau này có) trên đúng
        // (SectionId, Direction) → dùng giá trị đó.
        // Không có override → fallback về Section.MinRunningTimeMinutes[direction] (giá trị đã dedupe).
```

`MinimumTimetableBuilder` nhận thêm `ISectionRunningTimeResolver` qua constructor (cùng cách với
`IStopRules`/`IRunningTimeRules` hiện tại), gọi `resolver.GetMinRunningTimeMinutes(section,
service.Direction, service)` thay cho `section.GetMinRunningTimeMinutes(direction)` trực tiếp.
`UniformSectionRunningTimeResolver` (implementation mặc định) chỉ delegate thẳng ra `Section` — tương
thích ngược 100% với toàn bộ test Phase 2 hiện có khi mọi section đều uniform.

**Chưa code phần 15.8 ở thời điểm này** — chỉ code khi 15.7 chạy trên dữ liệu thật và thực sự phát hiện
ít nhất một section non-uniform, tránh over-engineering một cấu trúc chưa có bằng chứng cần dùng.

### 15.9 Output DTO — ghi lại kết quả solver vào database

Sau khi engine (Phase 2 hiện tại, và sau này solver Phase 3+) tính xong trajectory, với mỗi
`(TrainCode, JourneySequence)` phải xuất lại được:

```text
TimetableOutputRow                // ghi thẳng vào ĐÚNG 4 cột schema thật (mục 15.1), khớp kiểu SQL
  TrainCode: string
  JourneySequence: int
  ArrivalTime: TimeSpan?          // time(7) — suy từ TimetableEntry.ArrivalTimeMinutes, mục 15.3
  ArrivalDayNumber: int?          // DayNumber suy từ TimetableEntry.ArrivalTimeMinutes, mục 15.3
  DepartureTime: TimeSpan?        // time(7) — suy từ TimetableEntry.DepartureTimeMinutes
  DepartureDayNumber: int?        // DayNumber suy từ TimetableEntry.DepartureTimeMinutes
  CalculatedStopDuration: int     // = TimetableEntry.StopDurationMinutes
  RecoveryTimeFromPrev: int       // = TimetableEntry.RecoveryTimeFromPrevMinutes
  StopType: string                // = TimetableEntry.StopType.ToString()
  IsForcedStop: bool?             // tùy chọn — chỉ có ý nghĩa sau Phase 3 (StopType = ForcedMeet/ForcedOvertake)
  ConflictOrDecisionRef: string?  // tùy chọn — id của Conflict/CandidateSolution sinh ra thay đổi (Phase 3+)
```

Suy `ArrivalDayNumber`/`ArrivalTime` (và tương tự cho Departure) từ `TimetableEntry.ArrivalTimeMinutes`
(absolute minutes, mốc 0 = đầu Day 0 — mục 15.3) bằng đúng công thức nghịch đảo ở mục 15.3, rồi đổi phút
nguyên sang `TimeSpan` khi ghi vào cột `time(7)`:

```text
DayNumber        = floor(AbsoluteMinutes ÷ 1440)                  // quy ước Day 0
TimeOfDayMinutes = positiveModulo(AbsoluteMinutes, 1440)          // định nghĩa positiveModulo ở mục 15.3
ArrivalTime      = TimeSpan.FromMinutes(TimeOfDayMinutes)         // phần giây luôn = 0 (domain chỉ có phút)
```

`ArrivalTime`/`ArrivalDayNumber` = NULL đúng tại `JourneySequence=1` (ga xuất phát không có Arrival);
`DepartureTime`/`DepartureDayNumber` = NULL đúng tại `JourneySequence=N` (ga đích không có Departure) —
đối xứng với input ở mục 15.1, và khớp với `TimetableEntry.ArrivalTimeMinutes`/`DepartureTimeMinutes`
vốn đã là `int?` với đúng quy ước null này (mục 1.5). Tại **mọi dòng trung gian** (`1<JourneySequence<N`),
cả 4 cột này PHẢI được ghi (overwrite) với giá trị vừa tính — đúng đối xứng với vai trò
`IgnoredAsInput / SolverOutput` đã định nghĩa ở mục 15.1: input thì bỏ qua giá trị cũ, output thì ghi đè
không điều kiện, không "giữ lại nếu đã có sẵn".

**Đã đơn giản đi so với bản nháp trước:** vì quy ước Day 0 nội bộ (mục 2, đã có sẵn trong `Domain`) và
quy ước Day 0 của DB thật (đã xác nhận: `DepartureDayNumber` tại ga xuất phát = 0) **giống hệt nhau**,
output mapper **không cần** một tham số `DayNumberBase` để dịch ngược — `DayNumber` tính được ở trên
chính là giá trị ghi thẳng vào cột `ArrivalDayNumber`/`DepartureDayNumber` của DB, không qua bước cộng/
trừ nào nữa. (Nếu sau này có một hệ thống tiêu thụ khác dùng quy ước 1-based, phép dịch `+1` chỉ cần áp
dụng ở đúng boundary ghi ra hệ thống đó, không lẫn vào logic tính toán nội bộ của Phase 2.5.)

### 15.10 `BufferAllocator` — phase nào chịu trách nhiệm cấy recovery vào trajectory

Đã có sẵn trong mục 4 (dòng ~429): *"`BufferAllocator` (Engine, Phase 2 khởi tạo + Phase 8 tối ưu) chịu
trách nhiệm: (a) tạo phân bổ recovery-time ban đầu... (b) `SlackReallocationStrategy`..."*. Cần làm rõ
lại cho khớp với những gì **đã thực sự implement** ở Phase 2 (đã commit) và mô hình dữ liệu vừa xác nhận:

- **"Phase 2 khởi tạo" ở mục 4 KHÔNG có nghĩa `MinimumTimetableBuilder` tự rải recovery** —
  `MinimumTimetableBuilder` (đã code, đã test, mục 15.6) chỉ tạo trajectory tối thiểu với
  `RecoveryTimeFromPrevMinutes = 0` khắp nơi. Đây **là** trạng thái "khởi tạo" đúng nghĩa — không phải
  giá trị tạm chờ một bước rải khác chạy tiếp ngay sau. Không có heuristic "rải đều"/"rải theo lịch sử
  xung đột" nào chạy trong Phase 2 hiện tại.
- **Một `BufferAllocator` thật sự** (quyết định rải `RecoveryTimeFromPrev` khác 0 ở đâu, TRƯỚC khi
  conflict resolution chạy, để cải thiện chất lượng — Objective 6, mục 20, "độ đều của recovery") **chưa
  được implement**, và **không bắt buộc cho tính đúng đắn** — nhưng đây **không phải một khẳng định suông
  dựa trên "trực giác số học"** (bản nháp trước bị đúng chỉ ra là thiếu chứng minh tường minh, xem review
  trước Phase 3): `BufferCalculator.ComputeBufferState(service, trajectory)` (đã code, đã test —
  `src/TrainTimetable.Engine/BufferCalculator.cs`, `TrajectoryPropagatorTests.
  InsertDelay_OnZeroRecoveryTrajectory_ConsumesUnallocatedBufferAcrossSequentialInsertsAndBlocksOverflow`)
  tách tường minh 3 thành phần luôn cộng đúng bằng `TotalBuffer`:

  ```text
  TotalBufferMinutes = AllocatedRecoveryMinutes + ConsumedBufferMinutes + UnallocatedBufferMinutes
  ```

  trong đó `TotalBufferMinutes` là **hằng số bất biến** qua mọi lần `InsertDelay` (tính từ
  `Σ(RunningTimeFromPrev − RecoveryTimeFromPrev + StopDuration)` trên toàn trajectory — đại lượng này
  không đổi vì mỗi đơn vị `RecoveryTimeFromPrev` bị `InsertDelay` tiêu thì `RunningTimeFromPrev` giảm
  đúng bằng đó, xem `TrajectoryPropagator.InsertDelay`), `AllocatedRecoveryMinutes` = tổng
  `RecoveryTimeFromPrev` hiện còn trên trajectory, `ConsumedBufferMinutes` =
  `trajectory.Last.CumulativeInsertedDelayMinutes` (tổng delay đã từng chèn thành công). Test đã chạy
  đúng kịch bản bắt buộc: `TotalBuffer=20`, `RecoveryTimeFromPrev=0` khắp nơi (không có gì để
  `BufferAllocator` cấy trước) → chèn 5 phút: `IsFeasible=true`, `UnallocatedBuffer` 20→15,
  `Arrival(destination) <= FixedArrivalTime`; chèn thêm 12 (cộng dồn 17): `UnallocatedBuffer` →3; chèn
  thêm 4: `IsFeasible=false` (đúng bằng chứng — không có bước nào âm thầm để `Arrival(destination)` vượt
  `FixedArrivalTime` mà vẫn báo feasible). Đây chính là bằng chứng thực nghiệm cho khẳng định
  **`TrajectoryPropagator.InsertDelay` xử lý đúng cả khi `RecoveryTimeFromPrevMinutes = 0` khắp nơi** —
  không cần `BufferAllocator` cấy trước bất kỳ gì để CORRECTNESS.
- Vậy `BufferAllocator` (rải initial + `SlackReallocationStrategy`, mục 4.2) là một cải tiến **chất
  lượng lịch chạy** (đọc dễ hơn cho dispatcher, chừa dư địa đều hơn cho các xung đột ở nhiều điểm khác
  nhau thay vì để 1 xung đột sớm có thể "ăn" gần hết cục buffer cuối), **không phải yêu cầu functional**.
  Theo đúng phân công đã có ở mục 4: phần rải ban đầu (nếu làm) và `SlackReallocationStrategy` đều là
  việc của **Phase 8** (tối ưu, sau khi đã có 1 nghiệm khả thi) — **không phải Phase 2.5**. Phase 2.5
  (mục này) chỉ có trách nhiệm dựng đúng trajectory tối thiểu với buffer chưa phân bổ (mục 15.6) làm điểm
  khởi đầu cho Phase 3+; **chưa** code `BufferAllocator` ở bất kỳ hình thức nào tại đây.

### 15.11 Các trường còn thiếu so với `TrainService` (không suy được từ bảng hành trình)

- `Priority`: bảng hành trình không có cột này → cần nguồn dữ liệu khác, hoặc mặc định tạm thời `1` cho
  tới khi có input rõ ràng — **không suy đoán** từ `TrainCode` (vd. đoán "SE" = ưu tiên cao) vì đây là
  business rule cần xác nhận, không phải quy luật kỹ thuật.
- `StationTrack`/`CanMeet`/`CanOvertake`/`NumberOfTracks` của `Section`: vẫn là input độc lập, câu hỏi
  mở 1–2 ở mục 14.2 **vẫn còn mở** — bảng hành trình chỉ cho lịch trình từng tàu, không cho năng lực
  tránh/vượt của ga hay số đường của khu gian.
- `RailwayNetwork.GetStationByCode(int code)`: **chưa tồn tại** trong `RailwayNetwork` đã commit (chỉ có
  `GetStation(int sequence)`, tra theo `Sequence` chứ không theo `Code`) — cần khi code mapper thật (mục
  15.4). Đồng thời `Station.Code` (đã commit) đang là `string` trong khi `StationCode` của DB là `int` —
  cần quyết định giữ `string` (mapper `ToString()` khi lookup) hay đổi kiểu, để dành xác nhận khi code
  Phase 2.5 thật, chưa tự ý đổi domain đã commit ở đây.

### 15.12 Cố tình CHƯA làm ở Phase 2.5 (tránh lấn Phase 3 / lấn việc chưa có bằng chứng cần)

- **Đã loại bỏ hẳn** ý tưởng bóc "existing scheduled slack/recovery" từ chênh lệch Arrival/Departure của
  raw data (bản nháp trước) — tiền đề đó sai (mục 15.1: không có Arrival/Departure trung gian trong
  input), không chỉ là "chưa làm".
- **Chưa** code `BufferAllocator` dưới bất kỳ hình thức nào (kể cả một bản "rải đều" đơn giản) — theo
  đúng phân công ở mục 15.10, đây là việc của Phase 8, không phải Phase 2.5.
- **Chưa** tạo bảng DB mới (`SectionRunningTime` hay tương đương) — map trực tiếp tại tầng mapping/Engine
  như mục 15.7–15.8, đúng yêu cầu.
- **Chưa** code `ISectionRunningTimeResolver` (mục 15.8) cho tới khi 15.7 xác nhận thực sự cần.
- **Chưa** code DB integration (đọc bảng hành trình thật / ghi output thật) hay bất kỳ phần nào của
  Phase 3 — mục 15 hiện tại vẫn thuần là spec/thiết kế.
