<?php
// BitFighters Launcher API - Val�s adatb�zis kapcsolattal
// Haszn�lja a users �s patchnotes t�bl�kat

header('Content-Type: application/json; charset=utf-8');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: POST, GET, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type, Authorization');

// OPTIONS k�r�s kezel�se (preflight)
if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    exit(0);
}

// Adatb�zis kapcsolat be�ll�t�sok
$servername = "mysql.rackhost.hu";
$username = "c86218BitFighter";
$password = "Alosos123";
$dbname = "c86218game_users";

// HTTP met�dus ellen�rz�se
$method = $_SERVER['REQUEST_METHOD'];

// Debug inform�ci�k gy�jt�se
$debug_info = [
    "method" => $method,
    "content_type" => $_SERVER['CONTENT_TYPE'] ?? 'not set',
    "php_version" => phpversion(),
    "timestamp" => date('Y-m-d H:i:s')
];

try {
    // K�r�s t�pusa szerint adatok kinyer�se
    if ($method === 'GET') {
        $data = $_GET;
    } else {
        $json_input = file_get_contents("php://input");
        if (!empty($json_input)) {
            $data = json_decode($json_input, true);
            if (json_last_error() !== JSON_ERROR_NONE) {
                throw new Exception("JSON parsing error: " . json_last_error_msg());
            }
        } else {
            $data = $_POST;
        }
    }

    // Action param�ter ellen�rz�se
    if (!isset($data['action']) || empty($data['action'])) {
        http_response_code(400);
        echo json_encode([
            "success" => false, 
            "message" => "Hi�nyz� 'action' param�ter",
            "available_actions" => [
                "login", "get_user_score", "get_user_score_by_id", 
                "update_user_score", "get_users", "get_leaderboard", "get_news", "test"
            ]
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }

    // Adatb�zis kapcsolat
    $conn = new mysqli($servername, $username, $password, $dbname);
    
    if ($conn->connect_error) {
        throw new Exception("Adatb�zis kapcsolat hiba: " . $conn->connect_error);
    }
    
    $conn->set_charset("utf8");

    // Action switch
    switch (strtolower($data['action'])) {
        
        case 'test':
            try {
                $users_result = $conn->query("SELECT COUNT(*) as user_count FROM users");
                $patchnotes_result = $conn->query("SELECT COUNT(*) as patchnotes_count FROM patchnotes");
                
                $users_row = $users_result->fetch_assoc();
                $patchnotes_row = $patchnotes_result->fetch_assoc();
                
                echo json_encode([
                    "success" => true,
                    "message" => "Adatb�zis kapcsolat �s teszt OK",
                    "database" => $dbname,
                    "user_count" => (int)$users_row['user_count'],
                    "patchnotes_count" => (int)$patchnotes_row['patchnotes_count'],
                    "timestamp" => date('Y-m-d H:i:s')
                ], JSON_UNESCAPED_UNICODE);
            } catch (Exception $e) {
                echo json_encode([
                    "success" => false, 
                    "message" => "Teszt hiba: " . $e->getMessage()
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
        
        case 'login':
            if (empty($data['username']) || empty($data['password'])) {
                http_response_code(400);
                echo json_encode([
                    "success" => false, 
                    "message" => "Hi�nyz� username vagy password param�ter"
                ], JSON_UNESCAPED_UNICODE);
                break;
            }
            
            try {
                // Felhaszn�l� lek�rdez�se a users t�bl�b�l
                $stmt = $conn->prepare("SELECT id, username, highest_score, password FROM users WHERE username = ?");
                $stmt->bind_param("s", $data['username']);
                $stmt->execute();
                $result = $stmt->get_result();
                
                if ($row = $result->fetch_assoc()) {
                    // Jelsz� ellen�rz�se (plain text - nem biztons�gos, de �gy m�k�dik)
                    if ($row['password'] === $data['password']) {
                        echo json_encode([
                            "success" => true,
                            "message" => "Sikeres bejelentkez�s",
                            "user" => [
                                "id" => (int)$row['id'],
                                "username" => $row['username'],
                                "highest_score" => (int)$row['highest_score'],
                                "created_at" => "2024-01-01 00:00:00" // Mivel nincs created_at mez� a t�bl�ban
                            ]
                        ], JSON_UNESCAPED_UNICODE);
                    } else {
                        echo json_encode([
                            "success" => false, 
                            "message" => "Hib�s felhaszn�l�n�v vagy jelsz�"
                        ], JSON_UNESCAPED_UNICODE);
                    }
                } else {
                    echo json_encode([
                        "success" => false, 
                        "message" => "Hib�s felhaszn�l�n�v vagy jelsz�"
                    ], JSON_UNESCAPED_UNICODE);
                }
                $stmt->close();
            } catch (Exception $e) {
                echo json_encode([
                    "success" => false, 
                    "message" => "Bejelentkez�si hiba: " . $e->getMessage()
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        case 'get_user_score':
            if (empty($data['username'])) {
                http_response_code(400);
                echo json_encode([
                    "success" => false, 
                    "message" => "Hi�nyz� username param�ter"
                ], JSON_UNESCAPED_UNICODE);
                break;
            }
            
            try {
                $stmt = $conn->prepare("SELECT id, username, highest_score FROM users WHERE username = ?");
                $stmt->bind_param("s", $data['username']);
                $stmt->execute();
                $result = $stmt->get_result();
                
                if ($row = $result->fetch_assoc()) {
                    echo json_encode([
                        "success" => true,
                        "message" => "Pontsz�m sikeresen lek�rdezve",
                        "user" => [
                            "id" => (int)$row['id'],
                            "username" => $row['username'],
                            "highest_score" => (int)$row['highest_score']
                        ]
                    ], JSON_UNESCAPED_UNICODE);
                } else {
                    echo json_encode([
                        "success" => false, 
                        "message" => "Felhaszn�l� nem tal�lhat�",
                        "username" => $data['username']
                    ], JSON_UNESCAPED_UNICODE);
                }
                $stmt->close();
            } catch (Exception $e) {
                echo json_encode([
                    "success" => false, 
                    "message" => "Lek�rdez�si hiba: " . $e->getMessage()
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        case 'get_news':
            try {
                $limit = isset($data['limit']) ? (int)$data['limit'] : 20;
                if ($limit > 100) $limit = 100;
                
                // H�rek lek�rdez�se a patchnotes t�bl�b�l - csak title �s created_at
                $stmt = $conn->prepare("SELECT id, title, created_at FROM patchnotes ORDER BY created_at DESC LIMIT ?");
                $stmt->bind_param("i", $limit);
                $stmt->execute();
                $result = $stmt->get_result();
                
                $news = [];
                while ($row = $result->fetch_assoc()) {
                    $news[] = [
                        "id" => (int)$row['id'],
                        "title" => $row['title'],
                        "content" => "", // �res content - csak title haszn�lata
                        "created_at" => $row['created_at']
                    ];
                }
                
                // Ha nincs h�r az adatb�zisban, alap�rtelmezett h�rt adunk vissza
                if (empty($news)) {
                    $news[] = [
                        "id" => 0,
                        "title" => "�dv�z�lj�k a BitFighters vil�g�ban!",
                        "content" => "",
                        "created_at" => date('Y-m-d H:i:s')
                    ];
                }
                
                echo json_encode($news, JSON_UNESCAPED_UNICODE);
                $stmt->close();
            } catch (Exception $e) {
                // Fallback h�rek
                echo json_encode([
                    [
                        "id" => 0,
                        "title" => "Hiba t�rt�nt",
                        "content" => "",
                        "created_at" => date('Y-m-d H:i:s')
                    ]
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        case 'get_users':
            try {
                $limit = isset($data['limit']) ? (int)$data['limit'] : 100;
                if ($limit > 500) $limit = 500;
                
                $stmt = $conn->prepare("SELECT id, username, highest_score FROM users ORDER BY highest_score DESC LIMIT ?");
                $stmt->bind_param("i", $limit);
                $stmt->execute();
                $result = $stmt->get_result();
                
                $users = [];
                while ($row = $result->fetch_assoc()) {
                    $users[] = [
                        "id" => (int)$row['id'],
                        "username" => $row['username'],
                        "highest_score" => (int)$row['highest_score']
                    ];
                }
                
                echo json_encode([
                    "success" => true,
                    "message" => "Felhaszn�l�k sikeresen lek�rdezve",
                    "users" => $users,
                    "count" => count($users)
                ], JSON_UNESCAPED_UNICODE);
                $stmt->close();
            } catch (Exception $e) {
                echo json_encode([
                    "success" => false, 
                    "message" => "Lek�rdez�si hiba: " . $e->getMessage()
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        case 'get_leaderboard':
            try {
                $limit = isset($data['limit']) ? (int)$data['limit'] : 10;
                if ($limit > 100) $limit = 100;
                
                $stmt = $conn->prepare("SELECT id, username, highest_score FROM users WHERE highest_score > 0 ORDER BY highest_score DESC LIMIT ?");
                $stmt->bind_param("i", $limit);
                $stmt->execute();
                $result = $stmt->get_result();
                
                $leaderboard = [];
                $rank = 1;
                while ($row = $result->fetch_assoc()) {
                    $leaderboard[] = [
                        "rank" => $rank,
                        "id" => (int)$row['id'],
                        "username" => $row['username'],
                        "highest_score" => (int)$row['highest_score']
                    ];
                    $rank++;
                }
                
                echo json_encode([
                    "success" => true,
                    "message" => "Ranglista sikeresen lek�rdezve",
                    "leaderboard" => $leaderboard,
                    "count" => count($leaderboard)
                ], JSON_UNESCAPED_UNICODE);
                $stmt->close();
            } catch (Exception $e) {
                echo json_encode([
                    "success" => false, 
                    "message" => "Ranglista lek�rdez�si hiba: " . $e->getMessage()
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        case 'update_user_score':
            if (empty($data['user_id']) || !isset($data['new_score'])) {
                http_response_code(400);
                echo json_encode([
                    "success" => false, 
                    "message" => "Hi�nyz� user_id vagy new_score param�ter"
                ], JSON_UNESCAPED_UNICODE);
                break;
            }
            
            try {
                $user_id = (int)$data['user_id'];
                $new_score = (int)$data['new_score'];
                
                // Jelenlegi pontsz�m lek�rdez�se
                $stmt = $conn->prepare("SELECT highest_score, username FROM users WHERE id = ?");
                $stmt->bind_param("i", $user_id);
                $stmt->execute();
                $result = $stmt->get_result();
                
                if ($row = $result->fetch_assoc()) {
                    $current_score = (int)$row['highest_score'];
                    $username = $row['username'];
                    
                    if ($new_score > $current_score) {
                        // Pontsz�m friss�t�se
                        $update_stmt = $conn->prepare("UPDATE users SET highest_score = ? WHERE id = ?");
                        $update_stmt->bind_param("ii", $new_score, $user_id);
                        
                        if ($update_stmt->execute()) {
                            echo json_encode([
                                "success" => true,
                                "message" => "Pontsz�m sikeresen friss�tve",
                                "user" => [
                                    "id" => $user_id,
                                    "username" => $username,
                                    "old_score" => $current_score,
                                    "new_score" => $new_score
                                ]
                            ], JSON_UNESCAPED_UNICODE);
                        } else {
                            echo json_encode([
                                "success" => false, 
                                "message" => "Hiba a pontsz�m friss�t�se sor�n"
                            ], JSON_UNESCAPED_UNICODE);
                        }
                        $update_stmt->close();
                    } else {
                        echo json_encode([
                            "success" => false,
                            "message" => "Az �j pontsz�m nem nagyobb a jelenlegin�l",
                            "current_score" => $current_score,
                            "submitted_score" => $new_score
                        ], JSON_UNESCAPED_UNICODE);
                    }
                } else {
                    echo json_encode([
                        "success" => false, 
                        "message" => "Felhaszn�l� nem tal�lhat�",
                        "user_id" => $user_id
                    ], JSON_UNESCAPED_UNICODE);
                }
                $stmt->close();
            } catch (Exception $e) {
                echo json_encode([
                    "success" => false, 
                    "message" => "Friss�t�si hiba: " . $e->getMessage()
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        default:
            http_response_code(400);
            echo json_encode([
                "success" => false, 
                "message" => "Ismeretlen action: " . $data['action'],
                "available_actions" => [
                    "login", "get_user_score", "get_user_score_by_id", 
                    "update_user_score", "get_users", "get_leaderboard", "get_news", "test"
                ]
            ], JSON_UNESCAPED_UNICODE);
            break;
    }

    // Kapcsolat bez�r�sa
    $conn->close();

} catch (Exception $e) {
    http_response_code(500);
    echo json_encode([
        "success" => false,
        "message" => "�ltal�nos hiba: " . $e->getMessage()
    ], JSON_UNESCAPED_UNICODE);
}
?>