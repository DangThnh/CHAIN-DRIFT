using Fusion;

// Cấu trúc gói dữ liệu bàn phím gửi qua mạng
public struct NetworkInputData : INetworkInput
{
    public float moveInput;       // Tiến/Lùi (-1 đến 1)
    public float steerInput;      // Trái/Phải (-1 đến 1)
    public NetworkBool isDrifting;// Phanh tay (Space) - Dùng NetworkBool thay cho bool thường
    public NetworkBool isTowPressed; // <-- THÊM DÒNG NÀY ĐỂ BẮT PHÍM F
}