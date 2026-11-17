# Astral Party Android Supporter 1.0.0

CharacterSelect와 SimpleStats를 같은 게임 런타임에 넣은 Android용 패치입니다. 

<img width="1280" height="576" alt="photo_2026-11-17_20-05-51" src="https://github.com/user-attachments/assets/9550a87b-2536-4db3-83a4-d80c1ca06a4b" /><br />
캐릭터 상세보기에서 자리 및 역할 설정.

<img width="544" height="465" alt="photo_2026-11-17_20-06-26" src="https://github.com/user-attachments/assets/ac70e92a-e336-4ba5-a419-913344221a17" /><br />
대기실 및 캐릭터 선택 중 유저정보 표시 및 탈출 가능.

<img width="1280" height="576" alt="photo_2026-11-17_20-06-06" src="https://github.com/user-attachments/assets/b1a9ca65-6d11-468a-954c-3a84ae9cde2d" /><br />
캐릭터 선택 정렬 및 랜덤 선택 가능.

- 캐릭터 선택 화면에 P1 카드 왼쪽에 P1·P2·P3·P4·Random·정렬, P4 카드 오른쪽에 Dealer·Support·Card·Tank를 세로로 배치합니다. 정렬 버튼으로 자리순, 역할순, 기본 순서를 차례로 전환합니다. 기본은 게임이 제공한 순서입니다. 랜덤 선택은 게임의 기본 선택 동작을 호출하며 확정 버튼은 누르지 않습니다.
- 대기실과 캐릭터 선택 화면에서 방 참가자의 승률·좋아요·매너와 공개된 최근 전적 요약을 표시합니다.
- 캐릭터 상세 화면에서 태그를 터치해 편집할 수 있습니다. 통계 상자를 터치하면 최근 전적이 펼쳐집니다. 선택 카드 하단에는 둥근 모서리 안쪽에 중앙 정렬한 태그 색 띠가 나옵니다. 상세 태그는 Steam판처럼 선택 상태를 녹색과 분홍색으로 구분합니다. 한글을 지원하는 글꼴을 찾으면 기본 버튼 이름을 한글로 표시합니다. 참가자 통계에는 친구 추가·차단·내보내기·방 닫기/나가기와 표시·이름 전환을 추가했습니다. 내보내기는 방장에게 대기실에서만 표시됩니다. 방 나가기 후 방 상태가 해제되면 이전 패널로 복귀합니다. 방 UI가 계속 남으면 방 목록 또는 홈으로 이동합니다. 이 배포본의 설정 저장과 글꼴 표시는 실기기에서 확인이 필요합니다.

## 적용과 복원

1. 게임을 휴대폰에서 한 번 실행해 리소스 다운로드를 완료한 뒤 종료합니다.
2. 휴대폰에서 개발자 옵션의 USB 디버깅을 켜고 Windows PC에 USB로 연결합니다. 휴대폰에 뜨는 USB 디버깅 허용 메시지를 승인합니다.
3. ZIP 전체를 풀고 Install.cmd를 실행합니다. ADB가 없으면 Google의 Android SDK 이용약관을 확인하고 다운로드에 동의합니다. 설치 프로그램이 ADB를 내려받고 적용을 이어갑니다.
4. `Apply complete`가 표시되면 게임을 시작합니다.

첫 설치 때 게임 원본을 Windows 사용자 로컬 앱 데이터에 저장합니다. Restore.cmd로 게임 패치를 제거하고 Inspect.cmd로 현재 상태를 확인할 수 있습니다. 복원 후에도 태그 설정과 PC 원본 백업은 남습니다. Windows가 휴대폰을 인식하지 못하는 경우 기기 제조사의 USB 드라이버가 필요할 수 있습니다.

Google의 Platform-Tools는 첫 실행 시 사용자 동의를 받아 다운로드합니다. [공식 다운로드와 이용약관](https://developer.android.com/tools/releases/platform-tools#downloads)

## 캐릭터 태그 설정

첫 캐릭터 선택 화면에서 게임의 앱 데이터 `files` 폴더에 `AstralParty.CharacterSelect.cfg`를 생성합니다. Steam 설정의 캐릭터 ID 목록을 `P1`, `P2`, `P3`, `P4`, `Dealer`, `Support`, `Card`, `Tank`에 옮깁니다. 각 줄은 `키=값` 형식입니다. 태그별 ID는 쉼표로 구분합니다. 표시 이름에는 `Label.P1` 같은 키를 사용합니다. 이전 설정의 사용자 지정·수동 정렬 모드는 기본으로 전환됩니다. 편집 후 게임을 재시작합니다. 태그 목록이 비어 있으면 랜덤 후보도 비어 있습니다.

원본 소스는 [CharacterSelect](https://github.com/KOKOTO-DEV/AstralParty_CharacterSelect)와 [SimpleStats](https://github.com/KOKOTO-DEV/AstralPartySimpleStats)입니다. 
