# Update Planner

파일·폴더를 지정한 배포 위치에 예약 복사하는 Windows 데스크탑 애플리케이션입니다.  
WPF (.NET Framework 4.7.2) 기반으로 작성되었습니다.

---

## 주요 기능

### 배포 경로 관리
- **폴더 추가** — 소스 폴더의 하위 파일·폴더 전체를 배포 폴더에 복사
- **파일 추가** — 지정한 파일 하나를 배포 폴더에 복사
- 소스 하나에 배포 위치 하나를 1:1로 매핑하며, 여러 쌍을 등록해 한 번에 실행 가능

### 예약 실행
- **1회 실행** — 지정한 날짜·시간에 단발 실행
- **매주 반복** — 선택한 요일마다 지정 시간에 실행
- **매달 반복** — 선택한 날짜마다 지정 시간에 실행
- 시작 일시 ~ 종료 일시 범위 지정 지원
- 예약 중 실시간 카운트다운 표시
- **지금 실행** 버튼으로 예약 없이 즉시 복사 가능

### 백그라운드 실행
- X 버튼 클릭 시 완전 종료 또는 트레이 최소화 선택
- 예약이 등록된 상태에서는 완전 종료 불가 (실수 방지)
- 트레이 아이콘에서 예약 취소·열기·종료 제어 가능
- 실행 완료·오류 시 트레이 풍선 알림

### 로그 관리
- 배포 실행마다 날짜별 로그 파일 자동 생성
- 파일명 형식: `log_YYYY-MM-DD_[소스폴더명].txt`
- 보관 일수를 설정하면 오래된 로그 파일 자동 삭제

### 설정 저장
- **임시저장** 버튼으로 현재 경로·예약 설정을 `settings.xml`에 저장
- 앱 재시작 시 이전 설정 자동 복구
- **초기화** 버튼으로 경로·예약·저장 파일 일괄 삭제 (확인 팝업 포함)

---

## 폴더 구조

```
UpdatePlanner/
├── MainWindow.xaml / .cs            # 메인 화면 — 예약·실행·UI 상태 관리
├── App.xaml / .cs
├── Dialogs/
│   ├── AddMappingDialog.xaml / .cs  # 배포 경로 추가 (폴더 또는 파일)
│   ├── ScheduleDetailDialog.xaml / .cs  # 예약 일시·반복 주기 설정
│   └── ExitDialog.xaml / .cs        # 종료 옵션 (완전 종료 / 백그라운드)
├── Models/
│   ├── DeployMapping.cs             # 배포 매핑 모델 (MappingType: Folder / File)
│   └── ScheduleConfig.cs            # 예약 설정 모델 + 다음 실행 시각 계산 로직
├── Services/
│   ├── FileCopyService.cs           # 비동기 파일·폴더 복사
│   └── LogService.cs                # 로그 파일 생성·정리
└── Properties/
    └── AssemblyInfo.cs
```

---

## 동작 흐름

```
[+ 폴더 추가 / + 파일 추가]
  └─ AddMappingDialog → 소스·배포 경로 입력 → 목록에 추가

[상세 설정]
  └─ ScheduleDetailDialog
       ├─ 시작 일시 설정
       ├─ 종료 일시 설정 (선택)
       └─ 반복 주기 설정 (1회 / 매주 / 매달)

[예약 시작]
  └─ GetNextFireTime() → 카운트다운 타이머 시작
       └─ 시각 도달 시 RunCopyAsync()
            ├─ 폴더 매핑 → FileCopyService.CopyAsync()       (하위 전체 복사)
            └─ 파일 매핑 → FileCopyService.CopyFileToFolderAsync() (단일 파일)
                 └─ 반복 설정이면 다음 실행 시각 재계산 후 대기
                    반복 없거나 종료 일시 초과 시 예약 종료
```

---

## 설정 파일 형식 (`settings.xml`)

```xml
<Settings>
  <Mappings>
    <Mapping>
      <Type>Folder</Type>       <!-- Folder 또는 File -->
      <Source>C:\update\MyApp</Source>
      <Dest>\\server\deploy</Dest>
    </Mapping>
  </Mappings>
  <Schedule>
    <StartDate>2026-05-21</StartDate>
    <Hour>14</Hour>
    <Minute>00</Minute>
    <EndDate></EndDate>         <!-- 비어 있으면 종료 일시 없음 -->
    <Repeat>Weekly</Repeat>     <!-- None / Weekly / Monthly -->
    <WeekDays>1,3,5</WeekDays>  <!-- 0=일 1=월 ... 6=토 -->
    <MonthDays>1,15</MonthDays>
  </Schedule>
  <RetentionDays>3</RetentionDays>
</Settings>
```

---

## 개발 환경

| 항목 | 내용 |
|---|---|
| 언어 | C# |
| 프레임워크 | .NET Framework 4.7.2 |
| UI | WPF |
| 빌드 | MSBuild / Visual Studio 2022 |
| 대상 OS | Windows |
