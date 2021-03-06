﻿using Android.App;
using Android.Text.Format;
using Java.Lang;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using Microsoft.Xna.Framework.Audio;
using System;
using System.Xml;
using System.Net.Sockets;

namespace XamarinPong
{
    public class MultiplyerPong : Microsoft.Xna.Framework.Game
    {
    
        public static bool inBackground = false;
        private GraphicsDeviceManager _graphics;
        private SpriteBatch mainBatch;
        private Agent leftPlayer, rightPlayer, humanPlayer, Opponent;
        private Ball ball;
        private SpriteFont scoreFont, debugFont, promptFont;
        private int leftPlayerScore, rightPlayerScore, gameScore, Sensivity, resumeTime = 1, gamePoints = 5;
        private Vector2 scorePosition, debugStringPosition, promtPosition;
        private TouchCollection touchCollection;
        private float AIspeed, ballSpeed = 18f, momentumInfluence = 0.05f, maxVerticalRatio = 1.25f, currentSpeedMod = 1f, speedGain = 1.02f;
        private bool pause = false, debugMode = false, mustReset = false, resetGame = false;
        private Color themeColor;
        private string difficulty = "Normal", playerSprite = "Paddle1", ballSprite = "Ball1", 
            pointText = "Point!", winText ="You win!", loseText= "Game over\nYou Lose", prompt ="";
        private Texture2D fieldSprite;
        private Random random;
        private SoundEffect hitSound, wonPointSound, lostPointSound, wonGameSound, lostGameSound, bounceSound;
        private SoundEffectInstance hit, wonPoint, lostPoint, wonGame, lostGame, bounce;

        public int ScreenWidth => _graphics.GraphicsDevice.Viewport.Width;
        public int ScreenHeight => _graphics.GraphicsDevice.Viewport.Height;

        //Networking
        public static NetworkStream NetStream;
        public static bool isHost;
        byte inByte, outByte;


        public MultiplyerPong()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            base.Initialize();
            readSettings();
            random = new Random();
            scorePosition = new Vector2(ScreenWidth / 2 - ScreenWidth / 20, 10);
            debugStringPosition = new Vector2(0, ScreenHeight - 24);
            promtPosition = new Vector2(ScreenWidth /2 - ScreenWidth/20, ScreenHeight / 2);

            //Initial ball direction
            ball.generateBallDirection();
            adjustBallDirection();

            if(isHost)
            {
                //Sync player positions
                NetStream.WriteByte(Settings.RightPaddle == true ? (byte)1 : (byte)0);
                //Sync initial ball direction
                NetStream.Write(new byte[] { (byte)ball.direction.X, (byte)ball.direction.Y }, 0, 2);
            }
            else
            {
                var paddle = NetStream.ReadByte();
                var buff = new byte[2];
                var ballDir = NetStream.Read(buff, 0, 2);
                
                //Sync player positions
                if (paddle == 1)
                {
                    humanPlayer = leftPlayer;
                    Opponent = rightPlayer;
                }
                else
                {
                    Opponent = leftPlayer;
                    humanPlayer = rightPlayer;
                }
                //Sync initial ball direction
                ball.direction = new Vector2(buff[0], buff[1]);
            }
        }

        protected override void LoadContent()
        {
            mainBatch = new SpriteBatch(GraphicsDevice);
            readSettings();
            scoreFont = Content.Load<SpriteFont>("Score");
            promptFont = Content.Load<SpriteFont>("Prompt");
            debugFont = Content.Load<SpriteFont>("DebugFont");
            fieldSprite = Content.Load<Texture2D>("Images/Field");
            loadAudio();

            leftPlayer = new Agent(new Point(0, ScreenHeight/2), //Center vertically
                new Point(ScreenWidth/30, ScreenHeight/5),        //Scale according to screen dimensions
                Content.Load<Texture2D>("Images/" + playerSprite));
            leftPlayer.Translate(0, -leftPlayer.Height / 2); //Adjust player position

            rightPlayer = new Agent(new Point(ScreenWidth, ScreenHeight / 2 - leftPlayer.Height / 2), 
                new Point(ScreenWidth / 30, ScreenHeight / 5),                             
                Content.Load<Texture2D>("Images/" + playerSprite));
            rightPlayer.Translate(-rightPlayer.Width , -leftPlayer.Width / 2); 

            ball = new Ball(new Point(ScreenWidth/2, ScreenHeight/2),
                    new Point(ScreenWidth / 30, ScreenWidth / 30),
                    Content.Load<Texture2D>("Images/" + ballSprite));
        }

        protected override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            
            //Handle Android back button
            if (GamePad.GetState(0).IsButtonDown(Buttons.Back))
            {
                Exit();
            }

            if (gameTime.TotalGameTime.Seconds == 0)
                resumeTime = 0;
            
            if (!pause && gameTime.TotalGameTime.Seconds > resumeTime)
            {
                if (mustReset) Reset();
                PlayerMovement();
                ballMovement(gameTime);
                OpponentMovement();
                BallInteraction();
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(themeColor);
            mainBatch.Begin();

            mainBatch.Draw(fieldSprite, new Rectangle(0, 0, ScreenWidth, ScreenHeight), new Color(255, 255, 255, 22));
            leftPlayer.Draw(mainBatch);
            rightPlayer.Draw(mainBatch);
            ball.Draw(mainBatch);
            mainBatch.DrawString(scoreFont, leftPlayerScore + "  |  " + rightPlayerScore + " " + difficulty + "\nScore: " + gameScore, scorePosition, Color.Black);
            mainBatch.DrawString(promptFont, prompt, promtPosition, Color.Black);
            if(debugMode)
            mainBatch.DrawString(debugFont, 
            ScreenWidth + "x" + ScreenHeight + " Model:" + playerSprite + " RPModel:" + rightPlayer.sprite.Name + " BallDir:" + ball.direction + "Elapsed-Resume: " + gameTime.TotalGameTime.Seconds + "-" + resumeTime
            ,debugStringPosition, Color.Black);

            mainBatch.End();

            base.Draw(gameTime);
        }

        public void PlayerMovement()
        {
            touchCollection = TouchPanel.GetState();
            if (touchCollection.Count > 0)
            {
                //Move player according to touch being over/underneath Y position
                //Only one finger considered(0 index)
                int displacement = ((int)touchCollection[0].Position.Y - humanPlayer.Center.Y) / Sensivity;

                //Prevent movement from being too sudden
                if (displacement > ScreenHeight / 12)
                    displacement = ScreenHeight / 12;
                else if (displacement < -ScreenHeight / 12)
                    displacement = -ScreenHeight / 12;
                else
                    humanPlayer.decayMomentum();

                humanPlayer.Translate(0, displacement);
                NetStream.WriteByte((byte)displacement);
            }

            //Keep in screen bounds
            if (humanPlayer.Y < 0)
                humanPlayer.Translate(0, -humanPlayer.Y);

            else if (humanPlayer.Y > ScreenHeight - humanPlayer.Height)
                humanPlayer.Translate(0, ScreenHeight - humanPlayer.Y - humanPlayer.Height);
        }


        public void OpponentMovement()
        {
            int displacement = NetStream.ReadByte();
            Opponent.Translate(0, displacement);

            //Keep in screen bounds
            if (Opponent.Y < 0)
                Opponent.Translate(0, -Opponent.Y);

            else if (Opponent.Y > ScreenHeight - Opponent.Height)
                Opponent.Translate(0, ScreenHeight - Opponent.Y - Opponent.Height);
        }

        public void BallInteraction()
        {
            //Collision check
            if(ball.Center.Y >= leftPlayer.Center.Y-leftPlayer.Height/2 && ball.Center.Y <= leftPlayer.Center.Y + leftPlayer.Height/2 
                && ball.Center.X < leftPlayer.Width + ball.Width/2)
            {
                hit.Play();
                ball.direction.X = -ball.direction.X;
                //Add some randomness to bounce direction
                ball.direction.Y = -leftPlayer.momentum * momentumInfluence + (float)random.NextDouble() * 0.5f - 0.25f;
                adjustBallDirection();
                currentSpeedMod *= speedGain; //Increase speed each time
                ball.X = leftPlayer.X + leftPlayer.Width;
                if (humanPlayer == leftPlayer)
                    gameScore += Settings.Difficulty;
            }

            if (ball.Center.Y >= rightPlayer.Center.Y - rightPlayer.Height / 2 && ball.Center.Y <= rightPlayer.Center.Y + rightPlayer.Height / 2
                && ball.Center.X > rightPlayer.Center.X -ball.Width/2 - rightPlayer.Width/2)
            {
                hit.Play();
                ball.direction.X = -ball.direction.X;
                ball.direction.Y = -rightPlayer.momentum * momentumInfluence;
                adjustBallDirection();
                currentSpeedMod *= speedGain; //Increase speed each time
                ball.X = rightPlayer.X - ball.Width;
                if (humanPlayer == rightPlayer)
                        gameScore += Settings.Difficulty;
            }
        }

        public void Reset()
        {
            leftPlayer.Y = ScreenHeight / 2;
            rightPlayer.Y = ScreenHeight / 2 - leftPlayer.Height / 2;
            ball.Position = new Point(ScreenWidth / 2, ScreenHeight / 2);
            ball.generateBallDirection();

            //Sync ball direction
            if (isHost)
            {
                NetStream.Write(new byte[] { (byte)ball.direction.X, (byte)ball.direction.Y }, 0, 2);
            }
            else
            {
                var buff = new byte[2];
                var ballDir = NetStream.Read(buff, 0, 2);
                ball.direction = new Vector2(buff[0], buff[1]);
            }

            currentSpeedMod = 1f;
            prompt = "";
            mustReset = false;

            if(resetGame)
                ResetGame();
        }

        public void ResetGame()
        {
            if(gameScore > Settings.highScore) 
                Settings.highScore = gameScore;

            resetGame = false;
            leftPlayerScore = rightPlayerScore = gameScore = 0;
        }

        public void ballMovement(GameTime gameTime)
        {
            //Keep in screen bounds and bounce
            if (ball.Y < 0)
            {
                bounce.Play();
                ball.Translate(0, -ball.Y);
                ball.direction.Y = -ball.direction.Y;
            }
            else if (ball.Y > ScreenHeight - ball.Height)
            {
                bounce.Play();
                ball.Translate(0, ScreenHeight - ball.Y - ball.Height);
                ball.direction.Y = -ball.direction.Y;
            }

            if (ball.X < 0)
            {
                if (humanPlayer != rightPlayer)
                {
                    lostPoint.Play();
                } 
                ScorePoint(gameTime, rightPlayer);
            }
            else if (ball.X > ScreenWidth - ball.Width)
            {
                if (humanPlayer != leftPlayer)
                {
                    lostPoint.Play();
                }
                ScorePoint(gameTime, leftPlayer);
            }

            adjustBallDirection();
            ball.direction.Normalize();
            ball.Translate(ball.direction * ballSpeed * currentSpeedMod);
        }

        //We want the ball to move mostly horizontally
        public void adjustBallDirection()
        {
            if (ball.direction.Y / ball.direction.X > maxVerticalRatio)
            {
                if (ball.direction.Y < 0)
                    ball.direction.Y = -ball.direction.X * maxVerticalRatio;
                else
                    ball.direction.Y = ball.direction.X * maxVerticalRatio;
            }
        }

        public void ScorePoint(GameTime gameTime, Agent player)
        {
            mustReset = true;
            if(player == leftPlayer)
            {
                leftPlayerScore++;
                if (humanPlayer == leftPlayer)
                {
                    wonPoint.Play();
                    gameScore += Settings.Difficulty * 10;
                }
                pauseForTime(gameTime, 1);
            }
            else if(player == rightPlayer)
            {
                rightPlayerScore++;
                if (humanPlayer == rightPlayer)
                {
                    wonPoint.Play();
                    gameScore += Settings.Difficulty * 10;
                }
                pauseForTime(gameTime, 1);
            }

            if ((leftPlayer == humanPlayer && leftPlayerScore == gamePoints) || (rightPlayer == humanPlayer && rightPlayerScore == gamePoints))
            {
                wonGame.Play();
                prompt = winText + "\n New highscore: " + gameScore;
                if (gameScore > Settings.highScore)
                {
                    Settings.highScore = gameScore;
                }
                    
                resetGame = true;
            }
            else if ((leftPlayer == Opponent && leftPlayerScore == gamePoints) || (rightPlayer == Opponent && rightPlayerScore == gamePoints))
            {
                lostGame.Play();
                prompt = loseText + "\n New highscore: " + gameScore;
                if (gameScore > Settings.highScore)
                {
                    Settings.highScore = gameScore;
                }
                resetGame = true;
            }
            else
                prompt = pointText;
        }

        public void pauseForTime(GameTime gameTime, int seconds)
        {
            resumeTime = (gameTime.TotalGameTime.Seconds + seconds) % 60;
        }

        public void loadAudio()
        {
            hitSound = Content.Load<SoundEffect>("Audio/hit");
            wonPointSound = Content.Load<SoundEffect>("Audio/wonpoint");
            lostPointSound = Content.Load<SoundEffect>("Audio/lostpoint");
            wonGameSound = Content.Load<SoundEffect>("Audio/wongame");
            lostGameSound = Content.Load<SoundEffect>("Audio/lostgame");
            bounceSound = Content.Load<SoundEffect>("Audio/bounce");

            hit = hitSound.CreateInstance();
            wonPoint = wonPointSound.CreateInstance();
            lostPoint = lostPointSound.CreateInstance();
            wonGame = wonGameSound.CreateInstance();
            lostGame = lostPointSound.CreateInstance();
            bounce = bounceSound.CreateInstance();
        }

        public void readSettings()
        {
            themeColor = new Color(Settings.R, Settings.G, Settings.B, 122);
            Sensivity = 10 - Settings.Sensivity;
            gamePoints = Settings.maxScore;
            debugMode = Settings.DebugMode == 0 ? false : true;

            if(!Settings.RightPaddle)
            {
                humanPlayer = leftPlayer;
                Opponent = rightPlayer;
            }
            else
            {
                Opponent = leftPlayer;
                humanPlayer = rightPlayer;
            }

            switch (Settings.Difficulty)
            {
                case 1 : { AIspeed = ScreenHeight * 0.01f; difficulty = "Easy"; } break;
                case 2 : { AIspeed = ScreenHeight * 0.012f; difficulty = "Normal"; } break;
                case 3 : { AIspeed = ScreenHeight * 0.014f; difficulty = "Hard"; } break;
                default: { AIspeed = ScreenHeight * 0.012f; difficulty = "Normal"; } break;
            }

            switch (Settings.player)
            {
                case 0: playerSprite = "Paddle1"; break;
                case 1: playerSprite = "Paddle2"; break;
                case 2: playerSprite = "Paddle3"; break;
                default: playerSprite = "Paddle1"; break;
            }

           switch (Settings.ball)
            {
                case 0: ballSprite = "Ball1"; break;
                case 1: ballSprite = "Ball2"; break;
                case 2: ballSprite = "Ball3"; break;
                default: ballSprite = "Ball1"; break;
            }
        }
    }
}
