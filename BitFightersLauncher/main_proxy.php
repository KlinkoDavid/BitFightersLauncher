<?php
// BitFighters Launcher API - Valós adatbázis kapcsolattal
// Használja a users és patchnotes táblákat

header('Content-Type: application/json; charset=utf-8');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: POST, GET, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type, Authorization');

// OPTIONS kérés kezelése (preflight)
if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    exit(0);
}

// Adatbázis kapcsolat beállítások
$servername = "mysql.rackhost.hu";
$username = "c86218BitFighter";
$password = "Alosos123";
$dbname = "c86218game_users";

// HTTP metódus ellenõrzése
$method = $_SERVER['REQUEST_METHOD'];

// Debug információk gyûjtése
$debug_info = [
    "method" => $method,
    "content_type" => $_SERVER['CONTENT_TYPE'] ?? 'not set',
    "php_version" => phpversion(),
    "timestamp" => date('Y-m-d H:i:s')
];

try {
    // Kérés típusa szerint adatok kinyerése
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

    // Action paraméter ellenõrzése
    if (!isset($data['action']) || empty($data['action'])) {
        http_response_code(400);
        echo json_encode([
            "success" => false, 
            "message" => "Hiányzó 'action' paraméter",
            "available_actions" => [
                "login", "get_user_score", "get_user_score_by_id", 
                "update_user_score", "get_users", "get_leaderboard", "get_news", "test"
            ]
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }

    // Adatbázis kapcsolat
    $conn = new mysqli($servername, $username, $password, $dbname);
    
    if ($conn->connect_error) {
        throw new Exception("Adatbázis kapcsolat hiba: " . $conn->connect_error);
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
                    "message" => "Adatbázis kapcsolat és teszt OK",
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
                    "message" => "Hiányzó username vagy password paraméter"
                ], JSON_UNESCAPED_UNICODE);
                break;
            }
            
            try {
                // Felhasználó lekérdezése a users táblából
                $stmt = $conn->prepare("SELECT id, username, highest_score, password FROM users WHERE username = ?");
                $stmt->bind_param("s", $data['username']);
                $stmt->execute();
                $result = $stmt->get_result();
                
                if ($row = $result->fetch_assoc()) {
                    // Jelszó ellenõrzése (plain text - nem biztonságos, de így mûködik)
                    if ($row['password'] === $data['password']) {
                        echo json_encode([
                            "success" => true,
                            "message" => "Sikeres bejelentkezés",
                            "user" => [
                                "id" => (int)$row['id'],
                                "username" => $row['username'],
                                "highest_score" => (int)$row['highest_score'],
                                "created_at" => "2024-01-01 00:00:00" // Mivel nincs created_at mezõ a táblában
                            ]
                        ], JSON_UNESCAPED_UNICODE);
                    } else {
                        echo json_encode([
                            "success" => false, 
                            "message" => "Hibás felhasználónév vagy jelszó"
                        ], JSON_UNESCAPED_UNICODE);
                    }
                } else {
                    echo json_encode([
                        "success" => false, 
                        "message" => "Hibás felhasználónév vagy jelszó"
                    ], JSON_UNESCAPED_UNICODE);
                }
                $stmt->close();
            } catch (Exception $e) {
                echo json_encode([
                    "success" => false, 
                    "message" => "Bejelentkezési hiba: " . $e->getMessage()
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        case 'get_user_score':
            if (empty($data['username'])) {
                http_response_code(400);
                echo json_encode([
                    "success" => false, 
                    "message" => "Hiányzó username paraméter"
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
                        "message" => "Pontszám sikeresen lekérdezve",
                        "user" => [
                            "id" => (int)$row['id'],
                            "username" => $row['username'],
                            "highest_score" => (int)$row['highest_score']
                        ]
                    ], JSON_UNESCAPED_UNICODE);
                } else {
                    echo json_encode([
                        "success" => false, 
                        "message" => "Felhasználó nem található",
                        "username" => $data['username']
                    ], JSON_UNESCAPED_UNICODE);
                }
                $stmt->close();
            } catch (Exception $e) {
                echo json_encode([
                    "success" => false, 
                    "message" => "Lekérdezési hiba: " . $e->getMessage()
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        case 'get_news':
            try {
                $limit = isset($data['limit']) ? (int)$data['limit'] : 20;
                if ($limit > 100) $limit = 100;
                
                // Hírek lekérdezése a patchnotes táblából - csak title és created_at
                $stmt = $conn->prepare("SELECT id, title, created_at FROM patchnotes ORDER BY created_at DESC LIMIT ?");
                $stmt->bind_param("i", $limit);
                $stmt->execute();
                $result = $stmt->get_result();
                
                $news = [];
                while ($row = $result->fetch_assoc()) {
                    $news[] = [
                        "id" => (int)$row['id'],
                        "title" => $row['title'],
                        "content" => "", // Üres content - csak title használata
                        "created_at" => $row['created_at']
                    ];
                }
                
                // Ha nincs hír az adatbázisban, alapértelmezett hírt adunk vissza
                if (empty($news)) {
                    $news[] = [
                        "id" => 0,
                        "title" => "Üdvözöljük a BitFighters világában!",
                        "content" => "",
                        "created_at" => date('Y-m-d H:i:s')
                    ];
                }
                
                echo json_encode($news, JSON_UNESCAPED_UNICODE);
                $stmt->close();
            } catch (Exception $e) {
                // Fallback hírek
                echo json_encode([
                    [
                        "id" => 0,
                        "title" => "Hiba történt",
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
                    "message" => "Felhasználók sikeresen lekérdezve",
                    "users" => $users,
                    "count" => count($users)
                ], JSON_UNESCAPED_UNICODE);
                $stmt->close();
            } catch (Exception $e) {
                echo json_encode([
                    "success" => false, 
                    "message" => "Lekérdezési hiba: " . $e->getMessage()
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
                    "message" => "Ranglista sikeresen lekérdezve",
                    "leaderboard" => $leaderboard,
                    "count" => count($leaderboard)
                ], JSON_UNESCAPED_UNICODE);
                $stmt->close();
            } catch (Exception $e) {
                echo json_encode([
                    "success" => false, 
                    "message" => "Ranglista lekérdezési hiba: " . $e->getMessage()
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        case 'update_user_score':
            if (empty($data['user_id']) || !isset($data['new_score'])) {
                http_response_code(400);
                echo json_encode([
                    "success" => false, 
                    "message" => "Hiányzó user_id vagy new_score paraméter"
                ], JSON_UNESCAPED_UNICODE);
                break;
            }
            
            try {
                $user_id = (int)$data['user_id'];
                $new_score = (int)$data['new_score'];
                
                // Jelenlegi pontszám lekérdezése
                $stmt = $conn->prepare("SELECT highest_score, username FROM users WHERE id = ?");
                $stmt->bind_param("i", $user_id);
                $stmt->execute();
                $result = $stmt->get_result();
                
                if ($row = $result->fetch_assoc()) {
                    $current_score = (int)$row['highest_score'];
                    $username = $row['username'];
                    
                    if ($new_score > $current_score) {
                        // Pontszám frissítése
                        $update_stmt = $conn->prepare("UPDATE users SET highest_score = ? WHERE id = ?");
                        $update_stmt->bind_param("ii", $new_score, $user_id);
                        
                        if ($update_stmt->execute()) {
                            echo json_encode([
                                "success" => true,
                                "message" => "Pontszám sikeresen frissítve",
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
                                "message" => "Hiba a pontszám frissítése során"
                            ], JSON_UNESCAPED_UNICODE);
                        }
                        $update_stmt->close();
                    } else {
                        echo json_encode([
                            "success" => false,
                            "message" => "Az új pontszám nem nagyobb a jelenleginél",
                            "current_score" => $current_score,
                            "submitted_score" => $new_score
                        ], JSON_UNESCAPED_UNICODE);
                    }
                } else {
                    echo json_encode([
                        "success" => false, 
                        "message" => "Felhasználó nem található",
                        "user_id" => $user_id
                    ], JSON_UNESCAPED_UNICODE);
                }
                $stmt->close();
            } catch (Exception $e) {
                echo json_encode([
                    "success" => false, 
                    "message" => "Frissítési hiba: " . $e->getMessage()
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

    // Kapcsolat bezárása
    $conn->close();

} catch (Exception $e) {
    http_response_code(500);
    echo json_encode([
        "success" => false,
        "message" => "Általános hiba: " . $e->getMessage()
    ], JSON_UNESCAPED_UNICODE);
}
?>