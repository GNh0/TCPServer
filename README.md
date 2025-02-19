Language : c#

Framework : .NET Core 8.0

Project : c# Class Library

목적 : 여러 프로젝트에서 사용하기 위한 TCPServer 라이브러리 구현

기능 :
1. List<byte[]> 형으로 CustomEndOfMessageBytes 를 지정하여 수신데이터 EndCode를 설정
2. DataReceived 데이터 수신 이벤트 연결 메서드 OnDataReceived(byte[] data, string clientIp = null)로 데이터를 전송한 클라이언트 ip주소 확인 가능
3. 1:N 통신 가능
4. 연결 되어있는 클라이언트 별 DataBuffer가 존재하여 클라이언트 별로 데이터 관리 가능, 동시에 여러 클라이언트에서 수신되어서 데이터 혼합이 되지않음
5. async,await으로 비동기식 데이터 처리 구현
6. BroadcastData 메서드로 연결되어있는 모든 클라이언트에게 데이터 송신 가능 
7. SendDataToClient 메서드로 연결되어있는 특정 클라이언트에게 데이터 송신 가능
