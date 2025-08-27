<?php
// MOCK API - BitFighters Launcher
// Ez egy egyszer� mock API, amely nem ig�nyel adatb�zist
// Csak tesztel�si c�lokra!

header('Content-Type: application/json; charset=utf-8');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: POST, GET, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type, Authorization');

// OPTIONS k�r�s kezel�se (preflight)
if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    exit(0);
}

// HTTP met�dus ellen�rz�se
$method = $_SERVER['REQUEST_METHOD'];

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

    // MOCK adatok
    $mock_users = [
        'testuser' => [
            'id' => 1,
            'username' => 'testuser',
            'password' => 'testpass',
            'highest_score' => 1500,
            'created_at' => '2024-01-15 10:30:00'
        ],
        'player1' => [
            'id' => 2,
            'username' => 'player1',
            'password' => 'pass123',
            'highest_score' => 2300,
            'created_at' => '2024-01-20 14:15:00'
        ],
        'gamer2024' => [
            'id' => 3,
            'username' => 'gamer2024',
            'password' => 'gaming',
            'highest_score' => 800,
            'created_at' => '2024-02-01 09:45:00'
        ]
    ];

    // Action switch
    switch (strtolower($data['action'])) {
        
        case 'test':
            echo json_encode([
                "success" => true,
                "message" => "MOCK API m�k�dik!",
                "database" => "mock_database",
                "user_count" => count($mock_users),
                "timestamp" => date('Y-m-d H:i:s'),
                "note" => "Ez egy mock API tesztel�si c�lokra"
            ], JSON_UNESCAPED_UNICODE);
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
            
            $username = $data['username'];
            $password = $data['password'];
            
            if (isset($mock_users[$username]) && $mock_users[$username]['password'] === $password) {
                $user = $mock_users[$username];
                echo json_encode([
                    "success" => true,
                    "message" => "Sikeres bejelentkez�s",
                    "user" => [
                        "id" => $user['id'],
                        "username" => $user['username'],
                        "highest_score" => $user['highest_score'],
                        "created_at" => $user['created_at']
                    ]
                ], JSON_UNESCAPED_UNICODE);
            } else {
                echo json_encode([
                    "success" => false, 
                    "message" => "Hib�s felhaszn�l�n�v vagy jelsz�",
                    "hint" => "Pr�b�ld: testuser/testpass vagy player1/pass123"
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
            
            $username = $data['username'];
            if (isset($mock_users[$username])) {
                $user = $mock_users[$username];
                echo json_encode([
                    "success" => true,
                    "message" => "Pontsz�m sikeresen lek�rdezve",
                    "user" => [
                        "id" => $user['id'],
                        "username" => $user['username'],
                        "highest_score" => $user['highest_score']
                    ]
                ], JSON_UNESCAPED_UNICODE);
            } else {
                echo json_encode([
                    "success" => false, 
                    "message" => "Felhaszn�l� nem tal�lhat�",
                    "username" => $username
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        case 'get_news':
            $limit = isset($data['limit']) ? (int)$data['limit'] : 20;
            if ($limit > 100) $limit = 100;
            
            $news = [
                [
                    "id" => 1,
                    "title" => "?? �dv�z�lj�k a BitFighters vil�g�ban!",
                    "content" => "A launcher sikeresen bet�lt�tt �s az API t�k�letesen m�k�dik! Ez egy teszt h�r, amely bizony�tja, hogy a kommunik�ci� a kliens �s a szerver k�z�tt zavartalan. K�sz�lj fel az epikus csat�kra!",
                    "created_at" => date('Y-m-d H:i:s')
                ],
                [
                    "id" => 2,
                    "title" => "?? API Teszt Sikeres",
                    "content" => "Minden API endpoint m�k�dik! A bejelentkez�s, pontsz�m lek�rdez�s, �s h�rek bet�lt�se mind flawlessly m�k�dik. Most m�r csak a j�t�kot kell elind�tani!",
                    "created_at" => date('Y-m-d H:i:s', strtotime('-2 hours'))
                ],
                [
                    "id" => 3,
                    "title" => "?? �j Funkci�k",
                    "content" => "A launcher most m�r t�mogatja a felhaszn�l�i profilokat, pontsz�m k�vet�st, �s automatikus friss�t�seket. �lvezd a gaming �lm�nyt!",
                    "created_at" => date('Y-m-d H:i:s', strtotime('-1 day'))
                ],
                [
                    "id" => 4,
                    "title" => "?? Leaderboard Aktiv�lva",
                    "content" => "Most m�r megtekintheted a ranglist�t �s �sszehasonl�thatod pontsz�maidat m�s j�t�kosokkal. Ki lesz a BitFighters bajnoka?",
                    "created_at" => date('Y-m-d H:i:s', strtotime('-2 days'))
                ],
                [
                    "id" => 5,
                    "title" => "?? Rendszer Optimaliz�ci�",
                    "content" => "A launcher teljes�tm�ny�t optimaliz�ltuk a gyorsabb bet�lt�s �s z�kken�mentes felhaszn�l�i �lm�ny �rdek�ben.",
                    "created_at" => date('Y-m-d H:i:s', strtotime('-3 days'))
                ]
            ];
            
            $news = array_slice($news, 0, $limit);
            echo json_encode($news, JSON_UNESCAPED_UNICODE);
            break;
            
        case 'get_users':
            $limit = isset($data['limit']) ? (int)$data['limit'] : 100;
            if ($limit > 500) $limit = 500;
            
            $users = [];
            foreach ($mock_users as $user) {
                $users[] = [
                    "id" => $user['id'],
                    "username" => $user['username'],
                    "highest_score" => $user['highest_score']
                ];
            }
            
            // Pontsz�m szerint rendez�s
            usort($users, function($a, $b) {
                return $b['highest_score'] - $a['highest_score'];
            });
            
            $users = array_slice($users, 0, $limit);
            
            echo json_encode([
                "success" => true,
                "message" => "Felhaszn�l�k sikeresen lek�rdezve",
                "users" => $users,
                "count" => count($users)
            ], JSON_UNESCAPED_UNICODE);
            break;
            
        case 'get_leaderboard':
            $limit = isset($data['limit']) ? (int)$data['limit'] : 10;
            if ($limit > 100) $limit = 100;
            
            $leaderboard = [];
            $rank = 1;
            foreach ($mock_users as $user) {
                if ($user['highest_score'] > 0) {
                    $leaderboard[] = [
                        "rank" => $rank,
                        "id" => $user['id'],
                        "username" => $user['username'],
                        "highest_score" => $user['highest_score']
                    ];
                    $rank++;
                }
            }
            
            // Pontsz�m szerint rendez�s
            usort($leaderboard, function($a, $b) {
                return $b['highest_score'] - $a['highest_score'];
            });
            
            // Rangok �jrasz�mol�sa
            for ($i = 0; $i < count($leaderboard); $i++) {
                $leaderboard[$i]['rank'] = $i + 1;
            }
            
            $leaderboard = array_slice($leaderboard, 0, $limit);
            
            echo json_encode([
                "success" => true,
                "message" => "Ranglista sikeresen lek�rdezve",
                "leaderboard" => $leaderboard,
                "count" => count($leaderboard)
            ], JSON_UNESCAPED_UNICODE);
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
            
            $user_id = (int)$data['user_id'];
            $new_score = (int)$data['new_score'];
            
            // User keres�se ID alapj�n
            $found_user = null;
            foreach ($mock_users as $username => $user) {
                if ($user['id'] === $user_id) {
                    $found_user = $user;
                    break;
                }
            }
            
            if ($found_user) {
                $current_score = $found_user['highest_score'];
                
                if ($new_score > $current_score) {
                    // MOCK-ban nem mentj�k el, csak visszaadjuk a v�laszt
                    echo json_encode([
                        "success" => true,
                        "message" => "Pontsz�m sikeresen friss�tve (MOCK)",
                        "user" => [
                            "id" => $user_id,
                            "username" => $found_user['username'],
                            "old_score" => $current_score,
                            "new_score" => $new_score
                        ]
                    ], JSON_UNESCAPED_UNICODE);
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

} catch (Exception $e) {
    http_response_code(500);
    echo json_encode([
        "success" => false,
        "message" => "Hiba: " . $e->getMessage()
    ], JSON_UNESCAPED_UNICODE);
}
?>