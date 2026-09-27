Lineage Client Inspector V2.5

[편집 범위 확대]
- 알려진 텍스트 확장자뿐 아니라 실제 내용이 텍스트로 판별되는 파일도 원본 수정 가능
- 바이너리 확장자(.class/.jar/.exe/.dll/.pak/.idx/.spr/.psc/이미지 등)는 직접 텍스트 편집 제외
- 20MB 이하 원본 파일만 직접 편집
- Ctrl+S 저장 / Esc 취소
- 최초 저장 시 .bak 자동 백업
- 원본 복원(.bak) + .before_restore 안전 백업

[Java 단일 파일 컴파일]
서버팩 폴더의 .java 파일에서
파일/내부경로 우클릭 → '이 Java 파일 컴파일'
- JDK javac.exe 자동 검색(JAVA_HOME 또는 PATH)
- EUC-KR / Java 8(-source 1.8 -target 1.8)
- 서버팩 bin + lib JAR + l1jserver.jar를 classpath로 사용
- 임시 폴더에서 먼저 컴파일
- 성공한 .class만 bin에 반영
- 기존 class는 최초 반영 시 .bak 생성
- 실패 시 기존 class 변경 없음
- 컴파일 로그를 오른쪽 화면에 실시간 표시

[서버 전체 컴파일]
파일/내부경로 우클릭 → '서버 전체 컴파일 + JAR 생성'
- src 아래 전체 .java 검색
- 서버팩 build.xml과 동일하게 EUC-KR / Java 8 기준으로 javac 실행
- lib 폴더 JAR를 classpath에 사용
- 전체 컴파일을 임시 폴더에서 수행
- 성공 후 jar.exe로 l1jserver.jar 임시 생성
- 컴파일과 JAR 생성이 모두 성공한 경우에만 기존 l1jserver.jar 교체
- 기존 JAR는 l1jserver.jar.bak_날짜시간 으로 자동 백업
- 실패 시 기존 l1jserver.jar 유지

[필요 조건]
- JRE만으로는 컴파일 불가
- JDK 8 설치 필요
- JAVA_HOME 또는 PATH에서 javac.exe와 jar.exe를 찾을 수 있어야 함

[기존 기능 유지]
- 클라 + 서버팩 + Navicat DB 통합 검색
- 검색 위치 실시간 표시
- 서버팩 PSC 검색
- 이름 복사
- 정확 검색
- 원본 수정/복원
