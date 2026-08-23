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
 ├─ HeadwayRules       (MeetHeadway, SameDirectionHeadway, OvertakeHeadway)
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
  AccumulatedBufferUsed: int    // tổng buffer đã tiêu tới ga này (từ đầu hành trình)
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
  ServiceA, CycleIndexA
  ServiceB, CycleIndexB
  SectionId (hoặc dải Section liên tiếp cho OVERTAKE)
  ConflictTimeWindow: (start, end)
  Severity / Difficulty   // xem mục 12
```

Một `Conflict` luôn tham chiếu tới **cặp instance cụ thể** (`Service + CycleIndex` cho mỗi bên) vì đó là
thứ va chạm nhau trên trục thời gian thực; nhưng khi resolve, quyết định sửa luôn ghi ngược lại
`TrainService` (bỏ `CycleIndex`, quy đổi thời điểm xung đột về chu kỳ 0) — xem mục 0.1 hệ quả 2–3 và mục
9.2.

Thiết kế 3 loại conflict dùng **chung 1 hạ tầng phát hiện** (interval overlap trên occupation) nhưng khác
điều kiện kích hoạt và khác Resolver — đúng yêu cầu "không xây kiến trúc chỉ dùng riêng cho MEET" (mục 14
đề bài).

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
`DecelerationPenalty=1`, `MeetHeadway=3`) nằm trong `Configuration`, **không hard-code** trong Engine —
Engine chỉ nhận `IStopRules`, `IRunningTimeRules`, `IHeadwayRules` qua constructor/tham số.

---

## 4. BufferAllocator & UsableSlack (mục 8, câu hỏi 10) — **đã viết lại theo phản hồi**

Bản đầu tiên gộp "usable slack sau xung đột" thành một công thức duy nhất. Cần tách rõ **3** khái niệm
khác nhau về bản chất, vì chúng có nguồn gốc và điều kiện sử dụng khác nhau:

```text
TotalBuffer(T)        = hằng số theo mục 3, cố định khi timetable đầu vào cố định.
UsedBuffer(T)         = Σ RecoveryTimeFromPrev đã cấy dọc trajectory hiện tại + tổng WaitingTime
                         phát sinh do resolve conflict.
RemainingBuffer(T)    = TotalBuffer(T) - UsedBuffer(T)     // con số TOÀN CỤC — KHÔNG BAO GIỜ dùng
                                                            //  trực tiếp để quyết định 1 conflict cụ thể.
```

### 4.1 ForwardSlack(T, k) — "usable slack sau xung đột", trần cứng, luôn an toàn để dùng ngay

```text
ForwardSlack(T, k) = FixedArrivalTime(T)
                    - CurrentDeparture(T, k)
                    - MinimumRemainingJourneyTime(T, k → destination)
```

Đây là **maximum forward delay mà hành trình có thể hấp thụ TỪ ga k trở đi**, tính trên cơ sở
"hiện đang rời k lúc nào, và phần còn lại nếu chạy đúng bằng tối thiểu (không dùng thêm recovery nào
downstream) sẽ mất bao lâu". Nó **không** phụ thuộc buffer đã được phân bổ thế nào ở các ga sau k (vì
`MinimumRemainingJourneyTime` là cận dưới tuyệt đối, không tính recovery đã hoạch định) — nói cách khác,
`ForwardSlack(T,k)` **loại trừ đúng phần buffer "nằm ở tương lai xa" mà đề bài cảnh báo ở mục 8** (ví dụ:
tàu còn 40 phút buffer nhưng 35 phút nằm sau xung đột hiện tại → không tự động coi 35 phút đó là dùng
được cho xung đột này; `ForwardSlack` trả lời chính xác câu hỏi này bằng công thức trên).

Vì tính an toàn (không phụ thuộc giả định về việc tái phân bổ ngược), **`RequiredShiftCalculator` mặc
định chỉ được phép tiêu tới `ForwardSlack(T,k)`** cho một xung đột tại k — đây là con số dùng trong mọi
kiểm tra feasibility ở mục 7, 8.

### 4.2 RedistributableSlack(T, k) — buffer đã cấy Ở PHÍA TRƯỚC k, có thể "mượn" nhưng phải re-validate

```text
RedistributableSlack(T, k) = Σ RecoveryTimeFromPrev(m)  với mọi ga m nằm TRƯỚC k trên trajectory hiện tại
```

Đây là phần recovery-time **đã hoạch định** ở các khu gian trước k. Về nguyên tắc có thể loại bỏ (không
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
từ trạng thái hiện tại), không phải `RemainingBuffer(T)` (toàn cục, có thể chứa phần "kẹt" ở tương lai
theo đúng nghĩa slack sau điểm k khác) hay phần "kẹt" ở quá khứ trước k (chỉ dùng được qua cơ chế
reallocation có kiểm chứng riêng, không mặc định).

`BufferAllocator` (Engine, Phase 2 khởi tạo + Phase 8 tối ưu) chịu trách nhiệm: (a) tạo phân bổ
recovery-time ban đầu hợp lý dọc hành trình khi build minimum/free-running timetable (vd. rải đều, hoặc
ưu tiên rải trước các ga hay xảy ra giao cắt — dùng lịch sử/heuristic), và (b) thực hiện
`SlackReallocationStrategy` (mục 4.2) sau khi đã có nghiệm khả thi, để cải thiện độ đều của recovery
(Objective 6, mục 20) hoặc để "giải cứu" một nhánh beam search suýt infeasible vì thiếu `ForwardSlack`
cục bộ.

---

## 5. Section Occupation & Conflict Detection (mục 10, 14 — câu hỏi 5,6,7)

### 5.1 Section occupation

Sinh từ `TrainInstance` (mục 1.4/1.6), không trực tiếp từ `TrainService`: với mỗi `ServiceId` và mỗi
`CycleIndex` còn nằm trong cửa sổ chu kỳ `[-K, K]` (mục 11), mỗi cặp `(TimetableEntry[i], TimetableEntry[i+1])`
của `TrainServiceTrajectory` (dịch `+CycleIndex×1440`) sinh
1 `SectionOccupation { SectionId, ServiceId, CycleIndex, Direction, EntryTime, ExitTime }`. Nói cách khác,
`ConflictDetector` **luôn chạy ở "chế độ cyclic"** ngay từ Phase 3 — không có một phiên bản "chỉ trong
ngày" tách riêng rồi mở rộng sau; xem mục 11 để biết cách giới hạn `K` sao cho vẫn hiệu quả.

### 5.2 Phát hiện MEET (ngược chiều)

Với mỗi Section, gom occupation của tất cả instance (mọi service × mọi CycleIndex trong cửa sổ); sort
theo `EntryTime`; sweep tuyến tính. Hai occupation
`A` (Inbound) và `B` (Outbound) trên cùng Section (mà `NumberOfTracks == 1`) xung đột MEET nếu:

```text
overlap(A, B) := max(A.EntryTime, B.EntryTime) < min(A.ExitTime, B.ExitTime)
```

Độ phức tạp: O(n log n) mỗi section (n = số occupation), tổng O(N log N) toàn tuyến — đủ nhanh cho vài
trăm tàu/ngày × 177 khu gian.

### 5.3 Phát hiện HEADWAY (cùng chiều, chỉ cần giãn cách)

Hai occupation cùng chiều trên cùng Section vi phạm nếu khoảng cách entry-entry (hoặc exit-exit, tuỳ rule
cấu hình — xem `HeadwayRules.SameDirectionHeadway`) nhỏ hơn ngưỡng, **nhưng vẫn giữ được thứ tự trước–sau
không đổi trong suốt Section** (tàu sau không "chạm" tàu trước ở điểm nào bên trong section, chỉ là chưa
đủ giãn cách tuyệt đối).

### 5.4 Phát hiện OVERTAKE (cùng chiều, đảo thứ tự)

Khác HEADWAY: OVERTAKE là khi tàu B (đi sau, thường ưu tiên cao hơn hoặc nhanh hơn) sẽ **đuổi kịp và cần
vượt lên trước** tàu A trong dải section chung. Thuật toán: đi dọc các ga chung của A, B theo chiều di
chuyển; theo dõi "ai đang ở phía trước theo thời gian" tại mỗi ga; nếu tại ga `p` train A đang trước
(`Arrival(A,p) < Arrival(B,p)`), nhưng tại ga `q > p` thứ tự đảo ngược
(`Arrival(B,q) + margin <= Arrival(A,q)` hoặc occupation của B trên section trước q chồng lấp/đứng ngay
sau occupation của A với khoảng cách âm) → đây là điểm cần OVERTAKE, đánh dấu dải section `[p, q]` là
vùng xung đột loại OVERTAKE. Về bản chất, OVERTAKE = một dạng đặc biệt/nghiêm trọng của HEADWAY (khi
giãn cách âm chứ không chỉ thiếu hụt), nên `ConflictAnalyzer` sẽ **luôn kiểm tra HEADWAY trước**, và nếu
mức vi phạm đủ lớn để chỉ giải bằng chờ tại chỗ là không đủ (phá vỡ hard constraint no-crossing) thì phân
loại lại thành `OVERTAKE`.

Cả 3 loại dùng chung interface `IConflictRule.Detect(occupations) -> List<Conflict>` để `ConflictDetector`
chạy nhiều rule song song trên cùng dữ liệu occupation, đúng yêu cầu kiến trúc mở ở mục 14.

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

Đây là hàm nghiệp vụ trung tâm, tách bạch theo `Conflict.Type`:

### 7.1 MEET tại ga S, tàu chờ = W, tàu đi qua = P

```text
ForcedStop(W, S) = (S không thuộc StopRequirements của W)   // true nếu W vốn chạy thông qua S

EarliestSafeDeparture(W, S) = Arrival(P, S) + MeetHeadway     // theo đúng công thức mục 6 đề bài
NaturalDeparture(W, S)      = Arrival(W,S,tự nhiên) + StopTime(W,S,tự nhiên hoặc 0 nếu qua thông)

ExtraWait(W, S) = MAX(0, EarliestSafeDeparture(W,S) - NaturalDeparture(W,S))

RequiredShift(W) =
    ExtraWait(W,S)
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
EarliestSafeDeparture(Slow, S) = Departure(Fast, S) + OvertakeHeadway
ExtraWait(Slow, S) = MAX(0, EarliestSafeDeparture(Slow,S) - NaturalDeparture(Slow,S))
RequiredShift(Slow) = ExtraWait(Slow,S) + [ForcedStop(Slow,S) ? Accel+Decel : 0]
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
6.   RequiredShift(W) <= ForwardSlack(W, S)  (mục 4.1)                       → nếu vượt, candidate infeasible
                                                                                theo mặc định (Phase 5-7);
                                                                                chỉ Phase 8 mới thử mượn thêm
                                                                                RedistributableSlack (mục 4.2).
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

`RequiredShiftCalculator` trả về `ShiftResult { IsFeasible, ShiftMinutes, ForcedStop, ViolatedConstraint? }`
— không tự quyết định chọn, chỉ tính toán chính xác cho `CandidateEvaluator` dùng.

**Vì sao điều kiện 6 (`RequiredShift <= ForwardSlack`) đủ để đảm bảo điều kiện 5 và tính hấp thụ được ở
mục 8:** theo định nghĩa `ForwardSlack(T,k) = FixedArrivalTime(T) - CurrentDeparture(T,k) -
MinimumRemainingJourneyTime(T,k→dest)`, nếu `delta <= ForwardSlack(T,k)` thì kể cả trong tình huống xấu
nhất — bỏ hết mọi recovery-time đã hoạch định ở các ga sau `k` và chạy đúng bằng tối thiểu suốt phần còn
lại — tàu vẫn đến đích không muộn hơn `FixedArrivalTime(T)`. Đây chính là lý do thuật toán propagation ở
mục 8 luôn tìm được cách hấp thụ hết `delta` mà **không cần biết trước** buffer downstream được phân bố cụ
thể ra sao — nó chỉ cần tồn tại (dưới dạng recovery đã cấy, hoặc dưới dạng "chưa cấy nhưng vẫn nằm trong
biên `MinimumJourneyTime`") đủ nhiều theo đúng số học ở trên.

---

## 8. Propagation dọc trajectory (mục 19 — câu hỏi 12)

Sau khi xác định `RequiredShift(W)` tại ga `S`, lan truyền như sau (đi từ `S` về phía đích của `W`):

```text
delta := RequiredShift
for station m from S to Destination(W):
    Departure(m) += delta          // (nếu m == S: set theo EarliestSafeDeparture; nếu m > S: cộng dồn)
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

Vì `RequiredShift` đã được kiểm tra `<= UsableSlack` trước khi commit, về mặt lý thuyết `delta` sẽ luôn
được hấp thụ hết trước khi chạm `FixedArrivalTime` — invariant này chính là điều `RequiredShiftCalculator`
phải đảm bảo (mục 7.3, điều kiện 6).

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

## 11. Cyclic 24h — chi tiết & điều chỉnh so với đề bài (mục 21 — câu hỏi 15)

Vì hành trình có thể dài hơn 1440 phút (mục 0), việc chỉ nhân bản Day-1/Day0/Day+1 (cửa sổ −24h→+48h)
**không đủ** để bắt hết các cặp tàu có thể chồng lấp qua nhiều ngày. Đề xuất tổng quát hoá:

```text
K := ceil( (MaxJourneyTimeOverAllTrains_minutes) / 1440 ) + 1
```

Nhân bản mỗi train pattern thành các instance tại offset `k × 1440` với `k ∈ [-K, K]`.

**Tối ưu quan trọng:** vì mọi instance là bản dịch chuyển y hệt của cùng một pattern (do yêu cầu steady-
state — lịch lặp lại giống hệt mỗi ngày), ta **chỉ cần kiểm tra các cặp trong đó ít nhất một bên là
instance k=0** (bản canonical). Xung đột giữa `instance(i, k=-1)` và `instance(j, k=1)` là hoàn toàn tương
đương (sau khi dịch +1440) với xung đột giữa `instance(i, k=0)` và `instance(j, k=2)`, vốn đã được xét khi
so `k=0` với `k=2`. Điều này giữ độ phức tạp ở mức tuyến tính theo K thay vì bậc hai.

```text
ValidateCyclicBoundary():
    với mỗi train T (k=0) và mỗi train T' bất kỳ (kể cả T'=T), với mọi k ∈ [-K,K]\{0}:
        so sánh occupation(T, k=0) với occupation(T', k) đã dịch thời gian
    → dùng CHUNG thuật toán sweep-line ở mục 5, chỉ mở rộng danh sách occupation đưa vào sweep.
```

Nghiệm cuối cùng công bố = trajectory của các instance `k=0` (đúng theo yêu cầu đề bài); các bản k≠0 chỉ
tồn tại trong bộ nhớ lúc detect/validate, không xuất ra.

**Hệ quả thiết kế:** `BeamSearchSolver` giải trên tập instance `k=0` của tất cả train pattern, nhưng
`ConflictDetector` mà nó gọi phải luôn chạy ở "chế độ cyclic" (tức luôn tính thêm occupation dịch chuyển
±K×1440 khi kiểm tra 1 section) — không phải một bước validate tách rời chạy sau cùng, mà là một phần
integral của detection ngay từ Phase 3. `ValidateCyclicBoundary()` ở tầng Validator (Phase 9) là bước
kiểm tra độc lập, chạy lại từ đầu để xác nhận, không tin tưởng mù quáng rằng solver đã làm đúng.

---

## 12. Các nguyên nhân có thể khiến toàn bộ timetable infeasible (câu hỏi 16)

1. `TotalBuffer(T) < 0` cho ít nhất 1 tàu — bản thân giờ đi/đến cố định đã không tự nhất quán với
   `MinimumJourneyTime` (chưa cần xét tàu khác).
2. Tại một xung đột, **không candidate station nào trong cửa sổ** thoả `CanMeet`/`CanOvertake` — cần mở
   rộng `CandidateWindow` (cấu hình) hoặc đây là giới hạn hạ tầng thật (khu gian quá dài không có ga tránh).
3. Với mọi candidate khả dĩ, `RequiredShift > UsableSlack` của cả hai tàu — không đủ quỹ thời gian để giải
   dù ở bất kỳ ga nào trong cửa sổ.
4. **Hiệu ứng dây chuyền**: giải xung đột A tiêu hết slack cần cho xung đột B ngay sau đó, và không nhánh
   beam search nào (trong `BeamWidth` × `LookAheadConflicts` đã thử) sống sót — biểu hiện bằng
   `frontier` rỗng sau một tầng.
5. **Giới hạn năng lực ga**: nhiều hơn `MaxSimultaneousTrains` của một ga cần đỗ/tránh cùng lúc tại cùng
   thời điểm (ví dụ 3 tàu cùng cần gặp nhau ở ga chỉ có 2 track).
6. **Vi phạm biên chu kỳ không thể sửa**: sau khi mở rộng theo mục 11, phát hiện một tàu tự xung đột với
   chính bản sao dịch 24h/48h/... của các tàu khác mà không còn slack để điều chỉnh (vì giờ đi/đến gốc là
   bất biến mỗi ngày).
7. Mâu thuẫn ưu tiên/priority tuyệt đối kết hợp ràng buộc cứng khác (ví dụ 2 tàu cùng "không được dừng ở
   bất kỳ đâu ngoài ga đã khai báo" và không có ga hợp lệ nào trong vùng giao nhau).

`TimetableValidator` (Phase 9, độc lập với solver — mục 24) phải phát hiện và **định vị chính xác** loại
nào trong 7 loại trên đang xảy ra, trả về `{Train, Station/Section, Constraint, Expected, Actual}` thay vì
chỉ báo "infeasible".

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
| 6 | Xung đột qua biên 23:xx Day0 / 00:xx Day+1 | ConflictDetector chế độ cyclic (mục 11) | 3, 9 |
| 7 | Tàu không đủ TotalBuffer | BufferCalculator báo infeasible ngay từ đầu, không đưa vào solver | 2 |
| 8 | Technical stop 20 phút | MinimumTimetableBuilder + StopRules | 2 |
| 9 | Passenger stop 3 phút | MinimumTimetableBuilder + StopRules | 2 |
| 10 | ForcedStop sinh +1/+2 phút | RunningTimeRules áp dụng đúng khi StopType chuyển từ Through → ForcedMeet/ForcedOvertake | 5 |

Bổ sung thêm (phát sinh từ phân tích ở trên, nên có thêm trước khi vào Phase 7):

| # | Kịch bản | Lý do thêm |
|---|----------|------------|
| 11 | Hành trình dài hơn 24h (vd 30h) với 2 tàu chạy hàng ngày | Kiểm chứng đúng mục 0 & 11 — cửa sổ nhân bản K>1, không chỉ Day±1 |
| 12 | Ga chỉ có `CanMeet=true` nhưng `CanOvertake=false` | CandidateGenerator lọc đúng theo `Conflict.Type` |
| 13 | 3 tàu cùng cần gặp nhau tại 1 ga chỉ có 2 track | Phát hiện đúng nguyên nhân infeasible #5 (mục 12) |
| 14 | Reallocate buffer sau khi có nghiệm khả thi (Phase 8) | BufferAllocator cải thiện phân bố recovery mà không phá vỡ nghiệm đã đúng |

---

## 14. Câu hỏi cần Product/User quyết định trước khi vào Phase 2

Đây là các quyết định **không thể suy ra từ bài toán**, cần xác nhận để tránh phải sửa domain model giữa
chừng:

1. **Ngôn ngữ/stack triển khai** — style định danh (`TrainId`, `PascalCase`) gợi ý C#/.NET, nhưng chưa có
   xác nhận. Cần biết để Phase 2 bắt đầu viết code thật.
2. **Nguồn dữ liệu 178 ga & khu gian** — đã có sẵn (CSV/DB/API) hay cần nhập tay/mock cho Phase 2–7 rồi
   nạp dữ liệu thật sau?
3. **Dữ liệu năng lực tránh/vượt từng ga** (`StationTrack`) — đã có, hay tạm thời giả định `CanMeet=true`
   cho tất cả ga và bổ sung sau?
4. **`SameDirectionHeadway` mặc định** — đề bài chỉ cho `MeetHeadway=3` và ngụ ý `OvertakeHeadway`,
   nhưng chưa có giá trị mặc định cho giãn cách cùng chiều thuần tuý (không overtake). Cần một con số khởi
   điểm (có thể tạm dùng chung giá trị với MeetHeadway, nhưng nên xác nhận).

Tôi đề xuất: mock dữ liệu ga/khu gian tối thiểu cho Phase 2–7 (đủ để chạy 14 unit test ở mục 13), và chỉ
cần chốt ngôn ngữ lập trình để bắt đầu code Phase 2.
