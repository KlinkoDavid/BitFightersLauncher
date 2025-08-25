<?php
header('Content-Type: application/json');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: POST');
header('Access-Control-Allow-Headers: Content-Type');

// MySQL Proxy API - Launcher szamara
$data = json_decode(file_get_contents("php://input"), true);

if (!isset($data['action'])) {
    http_response_code(400);
    echo json_encode(["success" => false, "message" => "Hiányzó action paraméter"]);
    exit;
}

$conn = new mysqli("mysql.rackhost.hu", "c86218BitFighter", "Alosos123", "c86218game_users");
if ($conn->connect_error) {
    http_response_code(500);
    echo json_encode(["success" => false, "message" => "Adatbázis hiba: " . $conn->connect_error]);
    exit;
}

switch ($data['action']) {
    case 'login':
        if (!isset($data['username']) || !isset($data['password'])) {
            http_response_code(400);
            echo json_encode(["success" => false, "message" => "Hiányzó username vagy password"]);
            exit;
        }
        
        $stmt = $conn->prepare("SELECT id, username, highest_score FROM users WHERE username = ? AND password = ?");
        $stmt->bind_param("ss", $data['username'], $data['password']);
        $stmt->execute();
        $result = $stmt->get_result();
        
        if ($row = $result->fetch_assoc()) {
            echo json_encode([
                "success" => true, 
                "user" => [
                    "id" => $row['id'],
                    "username" => $row['username'],
                    "highest_score" => $row['highest_score']
                ]
            ]);
        } else {
            echo json_encode(["success" => false, "message" => "Hibás felhasználónév vagy jelszó"]);
        }
        $stmt->close();
        break;
        
    case 'get_users':
        // Felhasználók listázása (jelszó nélkül!)
        $result = $conn->query("SELECT id, username, highest_score, profile_picture FROM users ORDER BY id");
        $users = [];
        
        while ($row = $result->fetch_assoc()) {
            $users[] = [
                "id" => $row['id'],
                "username" => $row['username'],
                "highest_score" => $row['highest_score'],
                "profile_picture" => $row['profile_picture']
            ];
        }
        
        echo json_encode([
            "success" => true, 
            "message" => "Felhasználók lekérdezve", 
            "users" => $users,
            "count" => count($users)
        ]);
        break;
        
    case 'test':
        // Kapcsolat teszt
        $result = $conn->query("SELECT COUNT(*) as count FROM users");
        $row = $result->fetch_assoc();
        echo json_encode([
            "success" => true, 
            "message" => "Adatbázis kapcsolat OK", 
            "user_count" => $row['count']
        ]);
        break;
        
    default:
        http_response_code(400);
        echo json_encode(["success" => false, "message" => "Ismeretlen action: " . $data['action']]);
}

$conn->close();
?>